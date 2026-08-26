using './main.bicep'

param appName = 'mifuturotrabajo'
param location = 'westeurope'
param staticWebAppLocation = 'westeurope'

// Serverless rather than free tier, and not by preference.
//
// The free-tier slot was held by the Phase 0 spike account. Deleting that
// account does not release the slot immediately - Azure still reported it as
// claimed 35 minutes later, and it can take hours. Serverless has no
// subscription-wide limit, costs cents per month at demo volume, and unblocks
// the deployment without breaking the near-zero-cost constraint.
//
// Moving to 'free' later means creating a new account: billing mode cannot be
// changed in place.
param cosmosBilling = 'serverless'

// F1 is free: no Always On, so the API cold-starts after ~20 minutes idle, and
// 60 CPU-minutes/day after which it returns 403 until midnight UTC. B1 removes
// both for roughly USD 13/month. Start on F1; change this one value if a cold
// start ever costs an impression.
param appServiceSku = 'F1'

// Empty provisions an Azure-managed sending domain, which works immediately but
// often lands in spam. Set your own domain to send from it - it needs the
// DKIM/SPF records ACS generates, added at your registrar.
param emailDomain = 'ideapixel.com.ar'

// The Function App lives in its own resource group: Azure refuses a Linux
// Consumption plan alongside the API's Linux F1 plan. Create it first:
//   az group create -n rg-mifuturotrabajo-func -l westeurope
param functionsResourceGroupName = 'rg-mifuturotrabajo-func'
