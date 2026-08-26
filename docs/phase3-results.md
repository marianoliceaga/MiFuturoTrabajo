# Phase 3 — CV upload with real file storage

**Date:** 26 August 2026
**Branch:** `claude/phase-3-cv-storage` (main checkout)
**Verified against:** real Azurite blob storage and the real Cosmos account, driven over HTTP

---

## Outcome

| | Before | After |
|---|---|---|
| Upload endpoint | `IFormFile`, no validation | validated by **file signature** |
| What is persisted | a blob **URL** | a blob **path** |
| Download | opens a stored URL against a **private** container | expiring SAS minted per request |
| Blob deleted with the record | **No** | Yes |
| Client can set the file reference | **Yes** | No — structurally impossible |
| Auth to storage | connection string only | **managed identity** or connection string |
| Unit tests | 52 | **71** |
| Integration tests | 32 | **43** |

---

## The thing that was actually broken

Phase 0 recorded CV upload as "mostly done in the working tree". Reading it against Phase 2's private container showed it could not have worked end to end:

- `UploadFileAsync` returned `blobClient.Uri.ToString()` and that URL was stored in `CVVersion.FileUrl`.
- The container is `PublicAccessType.None` (fixed in Phase 0, finding F5).
- The client did `window.open(cv.FileUrl)`.

So every download opened a private blob URL with no credentials — **403, every time**. Upload worked; retrieval could not. The two halves were never exercised together.

Verified after the change, on real Azurite:

```
[ok]  SAS URL returns the blob                     200
[ok]  bytes round-trip intact                      4096 of 4096 bytes
[ok]  Content-Disposition names the original file  attachment; filename="mi-cv-2026.pdf"
[ok]  served as a PDF                              application/pdf
[ok]  blob is private without the SAS              403     <- the old code path
```

The last line is the old behaviour, now asserted as a requirement.

---

## Path, not URL

`CVVersion.FileUrl` became `BlobPath`, joined by `FileName`, `ContentType` and `SizeBytes`.

The rename is the design. A URL in the database is a durable pointer that either works for everyone or works for no one. A path is meaningless without a credential, so:

- a leaked database row is not a leaked document;
- access is revoked by rotating a key or removing a role assignment, with no data migration;
- the link handed to a browser lives 15 minutes and then stops working.

`BlobPath` never appears in a DTO. Two tests hold that line — one asserts the serialised DTO does not contain it, the other asserts `ToEntity` cannot receive it.

---

## An injection hole closed on the way

`CVVersionController.Update` did this:

```csharp
cv.FileUrl = cvVersionDto.FileUrl;
```

Any authenticated user could `PATCH` their own CV row to point at any URL — including another user's blob path, had one leaked. The DTO no longer carries a file reference at all, so the mapper cannot set one and the controller has nothing to assign. It is closed by construction rather than by a check that a later edit could drop.

```
[ok]  another user cannot mint a link for this CV   404
[ok]  another user cannot delete this CV            404
[ok]  owner blob survived the intrusion attempt     200
```

---

## Validation reads the bytes

`CvFileValidator` is static, I/O-free, and covered by 14 unit tests. Nothing the client asserts is trusted: the browser's content type is trivially forged and the extension is the tail of a user-supplied string. The extension picks the candidate format; the leading bytes must agree.

| Check | Rule |
|---|---|
| Extension | `.pdf`, `.doc`, `.docx` |
| Size | 1 byte … 5 MB, matched by `RequestSizeLimit` and by the client's `OpenReadStream` cap |
| Signature | `%PDF-` · `PK\x03\x04` (OOXML) · `\xD0\xCF\x11\xE0…` (OLE2) |

A Word 2007+ file saved as `.doc` is OOXML rather than OLE2, so `.doc` accepts either — rejecting it would have been a correctness bug dressed up as strictness.

The rejection messages are written for whoever picked the file:

```
The file does not look like a PDF document. Renaming a file does not change its format.
'.exe' files are not accepted. Upload a PDF, DOC or DOCX.
The file is 8.2 MB. The limit is 5 MB.
```

The client surfaces the server's text rather than `EnsureSuccessStatusCode`'s "400 Bad Request", because the server is the only party that has read the bytes.

---

## Managed identity, with a local path that still works

`BlobStorageService` picks its credential from configuration, and `AccountUrl` wins when both are set, so a deployed environment cannot silently fall back to a key left behind in config.

| Setting | Credential | Download link |
|---|---|---|
| `AzureStorage:AccountUrl` | `DefaultAzureCredential` | **user-delegation SAS** — signed by a key Entra issues on demand, so removing the role assignment kills every link already handed out |
| `AzureStorage:ConnectionString` | shared key | service SAS — the Azurite path |

Both branches are real code on the same interface. Phase 7 sets `AccountUrl` and grants **Storage Blob Data Contributor**; nothing else changes.

---

## Two surfaces, one code path

`ApplicantController` and `CVVersionController` both expose CV versions — the profile page uses one, the CV list page the other. Rather than write the upload twice, the rules live in `ICvDocumentService` (validate → store → describe) and both controllers call it. Duplicating "validate, then store, then remember where" is exactly how two surfaces drift apart.

---

## Testing

The integration suite gained a `FakeBlobStorageService`, registered as a singleton and **inspectable**, so tests assert on what is in storage rather than on status codes alone — which is what makes "delete really deletes the blob" a testable claim instead of a hopeful one. The suite needs no emulator, same principle as the committed `appsettings.Testing.json` from Phase 1.

The fake cannot prove a SAS works, so a separate pass drove the running API over HTTP against real Azurite and real Cosmos: **26 checks, all passing**, including the byte-for-byte round trip, the response headers, the 403 without a SAS, and the 404 after delete.

**One correction to my own work:** two of those checks failed on the first run. The cause was my verification script, not the code — it looked up `Content-Disposition` case-sensitively and Azurite sends header names lowercase. The headers were correct all along. I probed the raw response before touching anything, which is the only reason the fix landed on the script rather than on working code.

---

## Also in this phase

- `BlobStorageService` is now a singleton holding one `BlobServiceClient`, instead of constructing a client (and a connection pool) per operation.
- Container creation happens once behind a gate, not on every upload.
- The "or paste a file URL" input is gone from the CV modal. It described a storage model that no longer exists, and it was the client half of the injection hole.
- The client's `OpenReadStream` cap dropped from 10 MB to 5 MB so an oversized file fails in the browser rather than after a full upload.
- Blob deletion is deliberately **after** `SaveChangesAsync` and never throws: a storage hiccup leaves a logged orphan worth a fraction of a cent, rather than a CV the user cannot delete.

---

## Carried forward

- **Per-user CV quotas belong to Phase 7**, with the other quotas. Nothing today caps how many CVs one account may store.
- The storage account itself does not exist yet — Phase 7 provisions it with Bicep, sets `AccountUrl`, and assigns the role. Until then, local runs use Azurite (`npx azurite`).
- `MiFuturoTrabajo.Api/appsettings.json` on the dev machine holds live-format JWT, Cosmos and Storage keys. It is **gitignored and has never been committed** — verified against full history — so this is a note, not a finding. Phase 7 replaces those with App Service application settings and managed identity.
- Your `restart-services.ps1` stack was stopped to release locked DLLs during this phase; re-run that script to bring it back.
