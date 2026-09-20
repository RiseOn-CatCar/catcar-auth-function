variable "application_name" {
  description = "DNS-safe application name."
  type        = string
  default     = "catcar"
}

variable "environment" {
  description = "Deployment environment."
  type        = string
  default     = "prod"
}

variable "resource_group_name" {
  description = "Resource group for CatCar infrastructure."
  type        = string
  default     = "catcar-prod"
}

variable "vnet_name" {
  description = "VNet provisioned by foundation infrastructure."
  type        = string
  default     = "vnet-catcar-prod"
}

variable "functions_subnet_name" {
  description = "Subnet delegated to Microsoft.App/environments for Azure Functions."
  type        = string
  default     = "snet-functions"
}

variable "tags" {
  description = "Additional tags applied to managed Azure resources."
  type        = map(string)
  default     = {}
}
