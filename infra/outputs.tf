output "container_app_id" {
  value       = azurerm_container_app.function.id
  description = "Function Container App resource ID."
}

output "container_app_name" {
  value       = azurerm_container_app.function.name
  description = "Function Container App name."
}

output "identity_id" {
  value       = azurerm_user_assigned_identity.function.id
  description = "Function managed identity resource ID."
}
