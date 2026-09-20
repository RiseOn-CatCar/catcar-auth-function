terraform {
  required_version = ">= 1.5.0"

  backend "azurerm" {}

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
}

provider "azurerm" {
  features {}
}

data "azurerm_client_config" "current" {}

data "azurerm_resource_group" "shared" {
  name = var.resource_group_name
}

data "azurerm_virtual_network" "shared" {
  name                = var.vnet_name
  resource_group_name = data.azurerm_resource_group.shared.name
}

data "azurerm_subnet" "functions" {
  name                 = var.functions_subnet_name
  virtual_network_name = data.azurerm_virtual_network.shared.name
  resource_group_name  = data.azurerm_resource_group.shared.name
}

data "azurerm_key_vault" "shared" {
  name                = "kv-catcar-prod-${substr(data.azurerm_client_config.current.subscription_id, 0, 8)}"
  resource_group_name = data.azurerm_resource_group.shared.name
}

data "azurerm_application_insights" "shared" {
  name                = "appi-catcar-prod"
  resource_group_name = data.azurerm_resource_group.shared.name
}

locals {
  name_prefix = "${var.application_name}-${var.environment}"
  sub_suffix  = substr(data.azurerm_client_config.current.subscription_id, 0, 8)
  tags = merge(var.tags, {
    application = var.application_name
    environment = var.environment
    managed_by  = "terraform"
  })
}

resource "azurerm_storage_account" "function_storage" {
  name                     = "stfuncauth${local.sub_suffix}"
  resource_group_name      = data.azurerm_resource_group.shared.name
  location                 = data.azurerm_resource_group.shared.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  https_only               = true
  min_tls_version          = "TLS1_2"
  tags                     = local.tags
}

resource "azurerm_storage_container" "pkg" {
  name                  = "pkg"
  storage_account_id    = azurerm_storage_account.function_storage.id
  container_access_type = "private"
}

resource "azurerm_service_plan" "flex" {
  name                = "asp-${local.name_prefix}-auth"
  resource_group_name = data.azurerm_resource_group.shared.name
  location            = data.azurerm_resource_group.shared.location
  os_type             = "Linux"
  sku_name            = "FC1"
  tags                = local.tags
}

resource "azurerm_function_app_flex_consumption" "this" {
  name                        = "func-catcar-prod-${local.sub_suffix}"
  resource_group_name         = data.azurerm_resource_group.shared.name
  location                    = data.azurerm_resource_group.shared.location
  service_plan_id             = azurerm_service_plan.flex.id
  storage_container_type      = "blobContainer"
  storage_account_name        = azurerm_storage_account.function_storage.name
  storage_container_path      = "${azurerm_storage_account.function_storage.primary_blob_endpoint}pkg"
  storage_authentication_type = "SystemAssignedIdentity"
  virtual_network_subnet_id   = data.azurerm_subnet.functions.id
  https_only                  = true
  tags                        = local.tags

  site_config {
    application_insights_connection_string = data.azurerm_application_insights.shared.connection_string

    app_service_logs {
      disk_quota_mb         = 35
      retention_period_days = 7
    }
  }

  runtime_name          = "dotnet-isolated"
  runtime_version       = "10.0"
  instance_memory_in_mb = 512

  scale_control {
    maximum_instance_count = 2
  }

  identity {
    type = "SystemAssigned"
  }

  app_settings = {
    "FUNCTIONS_WORKER_RUNTIME"           = "dotnet-isolated"
    "ConnectionStrings__catcar-auth"     = "@Microsoft.KeyVault(VaultName=${data.azurerm_key_vault.shared.name};SecretName=postgres-auth-readonly-connection-string)"
    "CustomerJwt__SigningKey"            = "@Microsoft.KeyVault(VaultName=${data.azurerm_key_vault.shared.name};SecretName=customer-jwt-signing-key)"
    "Gateway__BackendKey"                = "@Microsoft.KeyVault(VaultName=${data.azurerm_key_vault.shared.name};SecretName=gateway-backend-key)"
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = data.azurerm_application_insights.shared.connection_string
  }
}

resource "azurerm_role_assignment" "function_kv_user" {
  scope                = data.azurerm_key_vault.shared.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_function_app_flex_consumption.this.identity[0].principal_id
}

resource "azurerm_role_assignment" "function_storage_blob_owner" {
  scope                = azurerm_storage_account.function_storage.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = azurerm_function_app_flex_consumption.this.identity[0].principal_id
}
