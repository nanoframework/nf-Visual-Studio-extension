# Shared code for the VS2019 extension (frozen copy)

The Visual Studio 2019 extension is in maintenance mode and will be removed.

This folder is a **frozen copy** of `vs-extension.shared`, taken when the VS2022/VS2026 extension started moving to
the VisualStudio.Extensibility model. Only `VisualStudio.Extension-2019` imports it
(`vs-extension.shared.vs2019.projitems`).

- `vs-extension.shared` now targets VS2022 and later only, and changes there don't reach VS2019.
- A fix that is also needed in VS2019 has to be applied here as well.
- Don't add features here: when VS2019 support is dropped, this folder is deleted together with the VS2019 project.
