using './main.bicep'

param appName = 'mifuturotrabajo'
param location = 'westeurope'
param staticWebAppLocation = 'westeurope'

// One free-tier Cosmos account per subscription. Set false if it is already
// spoken for; the account then falls back to provisioned throughput, which is
// no longer free.
param cosmosFreeTier = true

// F1 is free: no Always On, so the API cold-starts after ~20 minutes idle, and
// 60 CPU-minutes/day after which it returns 403 until midnight UTC. B1 removes
// both for roughly USD 13/month. Start on F1; change this one value if a cold
// start ever costs an impression.
param appServiceSku = 'F1'

// Empty provisions an Azure-managed sending domain, which works immediately but
// often lands in spam. Set your own domain to send from it - it needs the
// DKIM/SPF records ACS generates, added at your registrar.
param emailDomain = ''
