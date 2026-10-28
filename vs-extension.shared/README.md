# Shared code for the Visual Studio extension

Code shared by the extension projects for Visual Studio 2022 and later (`VisualStudio.Extension-2022`, which also serves
Visual Studio 2026), imported through `vs-extension.shared.projitems`.

The VS2019 extension doesn't use this folder: it has its own frozen copy in `VisualStudio.Extension-2019/Shared`
(see the README there). Code here can use APIs that VS2019 doesn't have, including the VisualStudio.Extensibility model.
A fix that is also needed in VS2019 has to be applied to that copy too.
