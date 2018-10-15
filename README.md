# Unity Package Exporter
This library will export a series of files from a Unity3D project into a Unity Package (.unitypackage). This is very useful for **automated builds** of packages using _app voyer_. I use this myself on my [Discord Rich Presence](https://github.com/Lachee/discord-rpc-csharp) library so I dont have to rebuild the package every time.

## Usage
Usage kinda simple. Its a command line interface with no GUI, so it might be a bit tricky to setup.
Here are the set of commands:
```
-output   The folder to output the package too.
            -output "mypackage.unitypackage"
            
-project  The path to the unity project root directory. This is the only required field.
            -project "D:\\User\\Lachee\\Documents\\Unity Project\\discord-rpc-csharp\\"
            
-asset    A individual asset to add to the pack. Can be added multiple times. Relative to project root.
            -asset "Assets\\Discord RPC\\DiscordManager.prefab" -asset "Assets\\Discord RPC\\ReadMe.rtf"
            
-assets   An array of assets to add to the pack. Can be added multiple times defined by CSV.
            -assets "Assets\\Discord RPC\\DiscordManager.prefab,Assets\\Discord RPC\\ReadMe.rtf"
            
-dir      A individual directory relative to the root to add. It will add all files and sub directories.
            -dir "Assets\\Discord RPC\\Editor\\"
            
-dirs      An array of directories relative to the root to add. It will add all files and sub directories.
            -dirs "Assets\\Discord RPC\\Editor\\,Assets\\Discord RPC\\Scripts\\"
            
-a        Adds all files in the asset folder.

-unpack   Unpacks a .unitypackage into the project before attempting to pack the actual target package.
            -unpack "Assets\\dependency_a.unitypackage" -unpack "Assets\\dependency_b.unitypackage"
```

## App Voyer
Here is a simple powershell buildscript that I use on my project:
```ps1
mkdir C:\projects\Unity-Package-Exporter
git clone https://github.com/Lachee/Unity-Package-Exporter.git C:\projects\Unity-Package-Exporter
cd C:\projects\Unity-Package-Exporter
dotnet run --project UnityPackageExporter -a -project "C:\\projects\\discord-rpc-csharp\\Unity Example\\" -output "C:\\projects\\discord-rpc-csharp\\DiscordRPC_Unity_Built.unitypackage"
```

The important part is the `dotnet run`. As you can see I do a standard run with dotnet, then define the project directory and output directory to be within my actual repo. Its also imporant to note that I cd into this folder.

**IMPORTANT**

This package builder _requires_ the `.meta` files unity generates to properly pack the assets. If you are ignoring them in the .gitignore, this may cause issues such as incorrect import settings and broken links after the build. Please make sure you _allow .meta files_ in your repository.

## Maintenance
This is a project I am using personally. I made it soley for this project. If you have any issues please make a new github issue, but I might not respond. I dont plan to actively maintain this as much as I do with my other library.
