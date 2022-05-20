# Unity Package Exporter
This library will export a series of files from a Unity3D project into a Unity Package (.unitypackage). This is very useful for **automated builds** of packages using _app voyer_. I use this myself on my [Discord Rich Presence](https://github.com/Lachee/discord-rpc-csharp) library so I dont have to rebuild the package every time.

## Usage
Now with the improved CLI provided by System.CommandLine, simply run the --help to get options.
```
-$ dotnet ../tools/UnityPackageExporter.dll --help
Description:
  Exports projects to Unity packages

Usage:
  UnityPackageExporter [options]

Options:
  -i, --input, --project <project>  Project to pack
  -o, --output <output>             Output package
  -a, --assets <assets>             Adds an asset to the pack. Supports glob matching. [default: **.*]
  -e, --exclude <exclude>           Excludes an asset from the pack. Supports glob matching. []
  --unpack <unpack>                 Unpacks an asset bundle before proceeding. Does not support glob matching. []
  --version                         Show version information
  -?, -h, --help                    Show help and usage information
```

## Github Actions

Below is a GitHub action for packaging a Unity Package styled project (no Assets folder).
```yml

jobs:
  env:
    package_path: "~/lachee-utilities.unitypackage"
  build:
    runs-on: ubuntu-latest
    steps:
      # Checks-out your repository under $GITHUB_WORKSPACE, so your job can access it
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v2

      # Install the packager. We are putting it outside the working directory so we dont include it by mistake
      - name: Install Unity Packager
        run: |
          git clone https://github.com/Lachee/Unity-Package-Exporter.git "../tools/unity-package-exporter"
          dotnet publish -c Release -o ../tools "../tools/unity-package-exporter/UnityPackageExporter"
        
      # Pack the assets
      - name: Package Project
        run: |
          echo "Creating package ${{env.package_path}}"
          dotnet ../tools/UnityPackageExporter.dll --project ./ --output package.unitypackage --exclude ".*" --exclude "Documentation"
        
      # Upload artifact
      - name: Upload Artifact
        uses: actions/upload-artifact@v3.0.0
        with:
          name: Unity Package
          path: ${{env.package_path}}   
```

**NOTE**

We are doing a publish then running the dll to avoid .NET bugs involving running projects.


**IMPORTANT**

This package builder _requires_ the `.meta` files unity generates to properly pack the assets. If you are ignoring them in the .gitignore, this may cause issues such as incorrect import settings and broken links after the build. Please make sure you _allow .meta files_ in your repository.

## Maintenance
This is a project I am using personally. I made it soley for this project. If you have any issues please make a new github issue, but I might not respond. I dont plan to actively maintain this as much as I do with my other library.
