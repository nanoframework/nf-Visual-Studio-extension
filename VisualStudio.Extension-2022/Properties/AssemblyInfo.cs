﻿//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Reflection;
using Microsoft.VisualStudio.Shell;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle(".NET nanoFramework VisualStudio Extension")]
[assembly: AssemblyCompany("nanoFramework")]
[assembly: AssemblyProduct(".NET nanoFramework VisualStudio Extension")]
[assembly: AssemblyCopyright("Copyright © 2019 .NET nanoFramework contributors")]
[assembly: System.CLSCompliant(false)]

#if DEBUG
// for debug build need to set these so binding redirects allow loading the correct library
[assembly: AssemblyVersion("9.99.999.0")]
[assembly: AssemblyFileVersion("9.99.999.0")]
[assembly: AssemblyInformationalVersion("9.99.999.0-DEBUG")]
#endif

[assembly: ProvideCodeBase(
    AssemblyName = @"CommunityToolkit.Mvvm",
    CodeBase = @"$PackageFolder$\CommunityToolkit.Mvvm.dll")]

[assembly: ProvideCodeBase(
    AssemblyName = @"Microsoft.Extensions.DependencyInjection",
    CodeBase = @"$PackageFolder$\Microsoft.Extensions.DependencyInjection.dll")]

[assembly: ProvideCodeBase(
    AssemblyName = @"Microsoft.Extensions.DependencyInjection.Abstractions",
    CodeBase = @"$PackageFolder$\Microsoft.Extensions.DependencyInjection.Abstractions.dll")]

[assembly: ProvideCodeBase(
    AssemblyName = @"nanoFramework.Tools.DebugLibrary.Net",
    CodeBase = @"$PackageFolder$\nanoFramework.Tools.DebugLibrary.Net.dll")]

[assembly: ProvideCodeBase(
    AssemblyName = @"nanoFramework.Tools.VS2022.Extension",
    CodeBase = @"$PackageFolder$\nanoFramework.Tools.VS2022.Extension.dll")]

[assembly: ProvideCodeBase(
    AssemblyName = @"CliWrap",
    CodeBase = @"$PackageFolder$\CliWrap.dll")]
