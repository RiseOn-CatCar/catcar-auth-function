terraform {
  required_version = ">= 1.5.0"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
}

data "azurerm_client_config" "current" {}

data "terraform_remote_state" "foundation" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.foundation_state_resource_group_name
    storage_account_name = var.foundation_state_storage_account_name
    container_name       = var.foundation_state_container_name
    key                  = var.foundation_state_key
    use_azuread_auth     = true
  }
}

data "terraform_remote_state" "database" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.database_state_resource_group_name
    storage_account_name = var.database_state_storage_account_name
    container_name       = var.database_state_container_name
    key                  = var.database_state_key
    use_azuread_auth     = true
  }
}

data "azurerm_resource_group" "foundation" {
  name = data.terraform_remote_state.foundation.outputs.resource_group_name
}

locals {
  foundation = data.terraform_remote_state.foundation.outputs
  database   = data.terraform_remote_state.database.outputs
  suffix     = substr(sha256("${data.azurerm_client_config.current.subscription_id}:${var.application_name}"), 0, 8)
  tags = {
    application = var.application_name
    environment = "azure"
    managed_by  = "terraform"
  }
}

resource "azurerm_user_assigned_identity" "function" {
  name                = "id-${var.application_name}-auth"
  resource_group_name = data.azurerm_resource_group.foundation.name
  location            = data.azurerm_resource_group.foundation.location
  tags                = local.tags
}

resource "azurerm_storage_account" "function" {
  name                            = coalesce(var.storage_account_name, "stcatauth${local.suffix}")
  resource_group_name             = data.azurerm_resource_group.foundation.name
  location                        = data.azurerm_resource_group.foundation.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  account_kind                    = "StorageV2"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  tags                            = local.tags
}

resource "azurerm_role_assignment" "storage" {
  scope                = azurerm_storage_account.function.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
}
resource "azurerm_role_assignment" "registry" {
  scope                = local.foundation.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
}
resource "azurerm_role_assignment" "vault" {
  scope                = local.database.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
}

resource "azurerm_container_app" "function" {
  name                         = local.foundation.auth_function_container_app_name
  container_app_environment_id = local.foundation.auth_function_environment_id
  resource_group_name          = data.azurerm_resource_group.foundation.name
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.function.id]
  }
  registry {
    server   = local.foundation.acr_login_server
    identity = azurerm_user_assigned_identity.function.id
  }
  # Deprecated: no longer read by the function code; remove together with the Azure release migration.
  secret {
    name                = "database-connection"
    key_vault_secret_id = local.database.postgres_auth_readonly_secret_id
    identity            = azurerm_user_assigned_identity.function.id
  }
  secret {
    name  = "os-service-internal-api-key"
    value = var.os_service_internal_api_key
  }
  secret {
    name                = "customer-jwt-key"
    key_vault_secret_id = local.database.customer_jwt_secret_id
    identity            = azurerm_user_assigned_identity.function.id
  }
  ingress {
    external_enabled = true
    target_port      = 80
    transport        = "http"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }
  template {
    # Scaling to zero saves idle compute but adds a cold start to the first request.
    min_replicas = 0
    max_replicas = 2
    http_scale_rule {
      name                = "http"
      concurrent_requests = "20"
    }
    container {
      name   = "auth-function"
      image  = var.auth_function_image
      cpu    = 0.25
      memory = "0.5Gi"
      startup_probe {
        transport = "TCP"
        port      = 80
      }
      liveness_probe {
        transport = "TCP"
        port      = 80
      }
      readiness_probe {
        transport = "TCP"
        port      = 80
      }
      env {
        name  = "FUNCTIONS_EXTENSION_VERSION"
        value = "~4"
      }
      env {
        name  = "FUNCTIONS_WORKER_RUNTIME"
        value = "dotnet-isolated"
      }
      env {
        name  = "DOTNET_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "AZURE_FUNCTIONS_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "AzureWebJobsStorage__accountName"
        value = azurerm_storage_account.function.name
      }
      env {
        name  = "AzureWebJobsStorage__credential"
        value = "managedidentity"
      }
      env {
        name  = "AzureWebJobsStorage__clientId"
        value = azurerm_user_assigned_identity.function.client_id
      }
      env {
        name        = "ConnectionStrings__catcar-auth"
        secret_name = "database-connection"
      }
      # These must be set before deploying the decoupled Function.
      env {
        name  = "OsService__BaseUrl"
        value = var.os_service_base_url
      }
      env {
        name        = "OsService__InternalApiKey"
        secret_name = "os-service-internal-api-key"
      }
      env {
        name        = "CustomerJwt__SigningKey"
        secret_name = "customer-jwt-key"
      }
      env {
        name  = "CustomerJwt__Issuer"
        value = "CatCar"
      }
      env {
        name  = "CustomerJwt__Audience"
        value = "CatCar.Api"
      }
      env {
        name  = "CustomerJwt__ExpirationMinutes"
        value = "60"
      }
      env {
        name  = "CustomerJwt__CorsOrigin"
        value = local.foundation.api_management_gateway_url
      }
      env {
        name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        value = local.foundation.application_insights_connection_string
      }
    }
  }
  lifecycle {
    precondition {
      condition     = startswith(var.auth_function_image, "${local.foundation.acr_login_server}/catcar-auth-function@sha256:")
      error_message = "The Function image must use the foundation ACR and catcar-auth-function repository."
    }
  }
  depends_on = [azurerm_role_assignment.storage, azurerm_role_assignment.registry, azurerm_role_assignment.vault]
}
