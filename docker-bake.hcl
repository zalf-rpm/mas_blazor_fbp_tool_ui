group "default" {
  targets = ["mas_fbp_flow_tool_ui"]
}

variable "TAG" {
  default = "latest"
}

target "mas_fbp_flow_tool_ui" {
  context    = "."
  dockerfile = "Dockerfile"
  tags       = ["zalfrpm/mas_fbp_flow_tool_ui:${TAG}", "zalfrpm/mas_fbp_flow_tool_ui:latest"]
  target     = "prod"
}
