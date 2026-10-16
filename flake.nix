{
  description = ".NET 10 SDK";

  inputs.nixpkgs.url = "nixpkgs";

  outputs = { self, nixpkgs }:
    let
      system = "x86_64-linux";
      pkgs = nixpkgs.legacyPackages.${system};
    in
    {
      devShells.${system}.default = pkgs.mkShell {
        # Falls das Attribut nicht existiert: nix search nixpkgs dotnet-sdk_10
        packages = [ pkgs.dotnetCorePackages.sdk_10_0 ];
        DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        DOTNET_NOLOGO = "1";
      };
    };
}
