output "function_app_name" {
  description = "Name of the provisioned Azure Function App."
  value       = azurerm_function_app_flex_consumption.this.name
}

output "function_app_default_hostname" {
  description = "Default hostname of the Azure Function App."
  value       = azurerm_function_app_flex_consumption.this.default_hostname
}

output "function_app_id" {
  description = "Resource ID of the Azure Function App."
  value       = azurerm_function_app_flex_consumption.this.id
}
