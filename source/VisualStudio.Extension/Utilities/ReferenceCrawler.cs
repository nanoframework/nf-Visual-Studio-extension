//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.References;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class ReferenceCrawler
    {

        /// <summary>
        /// Fills two dictionaries with the <see cref="ConfiguredProject"/> objects and their compiled output full path
        /// to make either one findable if we know the other one.
        /// </summary>
        /// <param name="configuredProjectsByOutputAssemblyPath">A dictionary to be filled for getting a
        /// <see cref="ConfiguredProject"/> object by its output assembly path.</param>
        /// <param name="outputAssemblyPathsByConfiguredProject">A dictionary to be filled for getting the compiled
        /// output path for a given <see cref="ConfiguredProject"/> object.</param>
        /// <returns>The task to be awaited.</returns>
        public static async Task CollectProjectsAndOutputAssemblyPathsAsync(
            IProjectService projectService,
            Dictionary<string, ConfiguredProject> configuredProjectsByOutputAssemblyPath,
            Dictionary<ConfiguredProject, string> outputAssemblyPathsByConfiguredProject)
        {
            // Loop through all projects which exist in the solution:
            foreach (UnconfiguredProject unconfiguredProject in projectService.LoadedUnconfiguredProjects)
            {
                // Get the right "configured" project, that is, the project in, for example, Debug/AnyCPU:
                ConfiguredProject configuredProject = await unconfiguredProject.GetSuggestedConfiguredProjectAsync();

                if (configuredProject != null)
                {
                    string path = await GetProjectOutputPathAsync(configuredProject);

                    // Skip projects that do not have target output like shared projects
                    if (string.IsNullOrEmpty(path))
                        continue;

                    configuredProjectsByOutputAssemblyPath.Add(path, configuredProject);
                    outputAssemblyPathsByConfiguredProject.Add(configuredProject, path);
                }
            }
        }

        /// <summary>
        /// Recursively steps down from a given project down by its referenced projects to collect the full paths of
        /// all assemblies which are used and thus need to be deployed, including the compiled output assemblies of the
        /// project as well as natively referenced assemblies such as NuGet packages used by the projects.
        /// </summary>
        /// <param name="configuredProjectsByOutputAssemblyPath">A filled dictionary over all
        /// <see cref="ConfiguredProject">ConfiguredProjects</see> in the solution indexed by their compiled output
        /// paths.</param>
        /// <param name="outputAssemblyPathsByConfiguredProject">A filled dictionary indexing all
        /// <see cref="ConfiguredProject">ConfiguredProjects</see> in the solution to find their compiled output paths.
        /// </param>
        /// <param name="assemblyPathsToDeploy">The set of full paths of all assemblies to be deployed which gets filled
        /// by this method.</param>
        /// <param name="project">The <see cref="ConfiguredProject"/> to start with. It gets added including its native
        /// references such as NuGet packages, and all of its directly or indirectly referenced projects get collected
        /// also.</param>
        /// <returns>The task to be awaited.</returns>
        public static async Task CollectAssembliesToDeployAsync(
            Dictionary<string, ConfiguredProject> configuredProjectsByOutputAssemblyPath,
            Dictionary<ConfiguredProject, string> outputAssemblyPathsByConfiguredProject,
            HashSet<string> assemblyPathsToDeploy,
            ConfiguredProject project)
        {
            string path;

            // Get the full path to the compiled assembly of this project by looking in the already collected
            // dictionary:
            path = outputAssemblyPathsByConfiguredProject[project];

            // Did we process this assembly already?
            if (assemblyPathsToDeploy.Add(path))
            {
                // We did not process this assembly yet, but now its output assembly path is included for deployment.

                // Collect the paths to all referenced assemblies of the configured project, such as NuGet references:
                foreach (IAssemblyReference assemblyReference in
                         await project.Services.AssemblyReferences.GetResolvedReferencesAsync())
                {
                    // As assemblyPathsToDeploy is a HashSet, the same path will not occure more than once even if added
                    // more than once by distinct projects referencing the same NuGet package for example.
                    assemblyPathsToDeploy.Add(await assemblyReference.GetFullPathAsync());
                }

                // Recursively process referenced projects in the solution:
                foreach (IBuildDependencyProjectReference projectReference in
                         await project.Services.ProjectReferences.GetResolvedReferencesAsync())
                {
                    // Get the path to the compiled output assembly of this project reference:
                    path = await projectReference.GetFullPathAsync();

                    // There should be a configured project for that path. As we collected all ConfiguredProjects and
                    // their output paths in advance, we can find the project by using the corresponding Dictionary.
                    if (configuredProjectsByOutputAssemblyPath.ContainsKey(path))
                    {

                        // Recursively process this referenced project to collect what assemblies it consists of:
                        await CollectAssembliesToDeployAsync(
                            configuredProjectsByOutputAssemblyPath,
                            outputAssemblyPathsByConfiguredProject,
                            assemblyPathsToDeploy,
                            configuredProjectsByOutputAssemblyPath[path]);

                    }
                    else
                    {
                        // If this ever happens, check whether the "same" path does come along in different casing.
                        // If so, consider improving this code to correctly handle that case.
                        throw new DeploymentException($"No ConfiguredProject found for output assembly path: {path}");
                    }
                }
            }
        }

        /// <summary>
        /// Gets the full path to the compiled output of a <see cref="ConfiguredProject"/>.
        /// </summary>
        /// <param name="project">The <see cref="ConfiguredProject"/> whose full ouput assembly path is wanted.</param>
        /// <returns>The full path of the compiled output assembly of the <paramref name="project"/>.</returns>
        public static async Task<string> GetProjectOutputPathAsync(ConfiguredProject project)
        {
            //... we need to access the target path using reflection (step by step)
            // get type for ConfiguredProject
            var projSystemType = project.GetType();

            // get private property MSBuildProject
            var buildProject = projSystemType.GetTypeInfo().GetDeclaredProperty("MSBuildProject");

            // get value of MSBuildProject property from ConfiguredProject object
            // this result is of type Microsoft.Build.Evaluation.Project
            var projectResult = await ((Task<Microsoft.Build.Evaluation.Project>)buildProject.GetValue(project));

            // we want the target path property
            return projectResult.Properties.First(p => p.Name == "TargetPath").EvaluatedValue;
        }
    }
}
