group "default" {
  targets = ["mas_blazor_fbp_tool_ui"]
}

variable "TAG" {
  default = "latest"
}

target "mas_blazor_fbp_tool_ui" {
  context    = "."
  dockerfile = "Dockerfile"
  tags       = ["zalfrpm/mas_blazor_fbp_tool_ui:${TAG}", "zalfrpm/mas_blazor_fbp_tool_ui:latest"]
  target     = "prod"
}
