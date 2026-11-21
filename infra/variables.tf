variable "application_name" {
  description = "Application name used in Azure resource names and tags."
  type        = string
  default     = "catcar"
}

variable "storage_account_name" {
  description = "Existing Function storage account name to adopt; null uses the deterministic default."
  type        = string
  default     = null
}

variable "foundation_state_resource_group_name" { type = string }
variable "foundation_state_storage_account_name" { type = string }
variable "foundation_state_container_name" { type = string }
variable "foundation_state_key" { type = string }
variable "database_state_resource_group_name" { type = string }
variable "database_state_storage_account_name" { type = string }
variable "database_state_container_name" { type = string }
variable "database_state_key" { type = string }

variable "auth_function_image" {
  type        = string
  description = "Immutable catcar-auth-function image in the foundation ACR, pinned by SHA-256 digest."
  validation {
    condition     = can(regex("^[a-z0-9]+\\.azurecr\\.io/catcar-auth-function@sha256:[0-9a-f]{64}$", var.auth_function_image))
    error_message = "Use an ACR catcar-auth-function image pinned by a lowercase SHA-256 digest."
  }
}

variable "os_service_base_url" {
  description = "OS service URL; must be configured before deploying the decoupled Function."
  type        = string
  default     = ""
}

variable "os_service_internal_api_key" {
  description = "Shared internal API key; must match the OS service key before deployment."
  type        = string
  sensitive   = true
  default     = ""
}
