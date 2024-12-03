# Developer notes

## Launching  Visual Studio experimental instance

To launch Visual Studio experimental instance:

1. Open VS developer command prompt.
2. Enter `"C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\Common7\IDE\devenv.exe"  /rootSuffix Exp`

> Mind to adjust the path above to your setup and Visual Studio version (2017 or 2019).

## Reset VS experimental instance

In case you need to reset the Visual Studio experimental instance:

1. Open VS developer command prompt.
2. Navigate to `C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VSSDK\VisualStudioIntegration\Tools\Bin`.
3. Enter `CreateExpInstance /Reset /VSInstance=17.0 /RootSuffix=_d9708c20Exp`.

> Mind to adjust the path above to your setup and Visual Studio version (16.0 for VS2019 and 17.0 for VS2022).
> The RootSuffix above (**_d9708c20Exp**) will be different for each installation. Please amend the above to match your local one. You can find the suffix by navigating to the following folder in the users folder `C:\Users\johndoe\AppData\Roaming\Microsoft\VisualStudio`.

You'll want to reset VS experimental instance on a number of situations. Usually this is when you need to start fresh because there is too much clutter, a failed deployment that is creeplying your debugging or whenever you install a new Visual Studio version.

## Debugging with the **nanoFramework** Debugger library

In situations where you want to debug something in the **nanoFramework** Debugger library please follow these steps:

1. Make sure to update (or checkout the appropriate commit) in the `nf-debugger` git sub-module.
1. Load the debugger Solution and perform the "NuGet package restore". You can close it after that.
1. Load the `nanoFramework.Tools.Debugger.sln` solution there, restore the NuGets for the solution and rebuild it. After this you can close the solution.
1. Load the **nanoFramework** extension solution in Visual Studio.
1. Expand the folder `debugger-library` and find there 2 projects for each of the components.
1. Right click and hit `Reload` for each of the project there. Like this.

    ![](images/reloading-debugger-projects.png)

1. Remove the `nf-debugger` Nuget package from the extension projects.
1. Add a reference to the `nf-debugger` project in the extension projects.
1. Build as ususal.
1. Perform whatever debug that you need by placing breakpoint on any source file of the debugger library.
1. When you are done remove the reference to the `nf-debugger` project and add back the Nuget package.
1. Unload the debugger library projects from the solution.

## Known issue with debugging in the experimental instance

Because of several `CodeBase` entries required for Visual Studio to load the extension assemblies (see [here](https://developercommunity.visualstudio.com/t/Image-icons-from-image-catalog-not-showi/10791720) for the details) you can run into issues for the experimental instance to load the actual assembly of the build you're trying to debug.
If that happens, you're forced to follow these steps:

1. Make a copy of the folder providing the targets and props of the project system. For Visual Studio 2022, this is usually located at `C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\nanoFramework\v1.0`. Delete the DLLs from there. Leave only the targets and props files.
1. Uninstall the official .NET nanoFramework extension from Visual Studio.
1. Load the extension solution and work as usual.

Before installing back the official .NET nanoFramework extension, mind to rename (or remove) the project system folder. Failing to do so will cause the extension install to fail.
