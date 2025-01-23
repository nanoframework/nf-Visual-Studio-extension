//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal class DeploymentAssemblyDistinctEquality : IEqualityComparer<DeploymentAssembly>
    {
        public bool Equals(DeploymentAssembly assembly1, DeploymentAssembly assembly2)
        {
            var name1 = System.IO.Path.GetFileName(assembly1.Path);
            var name2 = System.IO.Path.GetFileName(assembly2.Path);

            if ((name1 == name2) && (assembly1.Version == assembly2.Version))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public int GetHashCode(DeploymentAssembly assembly)
        {
            return System.IO.Path.GetFileName(assembly.Path).GetHashCode() ^ assembly.Version.GetHashCode();
        }
    }
}