//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    internal class DeploymentImageGenerator
    {
        // The "image-to-deploy" it's basically a binary file with everything that needs to flash an empty target.
        // This would be: 
        // - nanoBooter (for platforms that have it)
        // - nanoCLR
        // - deployment region with PEs of managed application and referenced assemblies
        // - configuration block (optional)
        // Because reading the flash is a long running operation we'll use a cache mechanism so we don't have to download it on every deployment.
        // The cached with the flash content for a target lives in the project bin folder with the following name pattern:
        // board name (like it shows on Device Explorer) plus the solution build info, e.g.:
        // "STM32 STLink @ COM46 - 0.9.99.999"


        /// <summary>
        /// Runs preparations to generate a deployment image.
        /// </summary>
        /// <param name="device">nanoDevice being deployed to.</param>
        /// <param name="configuredProject">The <see cref="ConfiguredProject"/> object that is being deployed.</param>
        /// <param name="outputPaneWriter">The output pane writer where to output progress messages.</param>
        /// <returns>A string with the path for the flash dump file. Empty if not applicable or the operation failed on any way.</returns>
        internal static async Task<string> RunPreparationStepsToGenerateDeploymentImageAsync(
            NanoDeviceBase device, 
            ConfiguredProject configuredProject, 
            TextWriter outputPaneWriter)
        {
            string targetFlashDumpFileName = "";
            string cacheLocation;

            await Task.Yield();

            if (device.DeviceInfo.SolutionBuildInfo.Contains("STM32") ||
                device.DeviceInfo.SolutionBuildInfo.Contains("DISCO") ||
                device.DeviceInfo.SolutionBuildInfo.Contains("NUCLEO"))
            {
                // this seems to be a STM32 nanoDevice

                MessageCentre.InternalErrorMessage("nanoDevice is STM32, checking for flash dump on cache.");

                // This can go wrong in many ways.
                // Because it's a subsidiary operation of "deploy" and, most important, it's not part of the standard "deploy" VS concept,
                // it's wrapped on a try catch block so the deployment can proceed without it.
                try
                {
                    // preference for flash dump cache
                    if (string.IsNullOrEmpty(NanoFrameworkPackage.SettingPathOfFlashDumpCache))
                    {
                        // flash dump is to be saved in project output path
                        cacheLocation = await GetProjectOutputPathAsync(configuredProject);
                    }
                    else
                    {
                        // flash dump is to be saved in user path
                        cacheLocation = NanoFrameworkPackage.SettingPathOfFlashDumpCache;
                    }

                    // build cache file name
                    targetFlashDumpFileName = Path.Combine(
                        cacheLocation,
                        device.Description + device.DeviceInfo.ClrBuildVersion.ToString(4) +
                        ".dumpcache");

                    // do we have a cached image of the device flash
                    if (!File.Exists(targetFlashDumpFileName))
                    {
                        MessageCentre.InternalErrorMessage("Couldn't find a flash dump for this nanoDevice. Setting up one now. This can take a couple of minutes...");
                        await outputPaneWriter.WriteLineAsync("Couldn't find a flash dump for this nanoDevice. Setting up one now. This can take a couple of minutes...");

                        // get memory map
                        var memoryMap = device.DebugEngine.GetMemoryMap();

                        // get flash map
                        var flashSectorMap = device.DebugEngine.GetFlashSectorMap();

                        // get flash start address (to start reading from)
                        var flashStartAddress = memoryMap.First(m => (m.m_flags & Commands.Monitor_MemoryMap.c_FLASH) == Commands.Monitor_MemoryMap.c_FLASH).m_address;

                        // Considering that (so far in all the targets currently active) the block storage configuration have the blocks ordered from bootloader to deploy,
                        // it's OK (and much efficient) to read the flash up the last CLR block. The deployment region will be filled in latter.

                        var lastClrBlock = flashSectorMap.Last(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE);
                        var lastClrBlockStartAddress = lastClrBlock.m_StartAddress;
                        var lastClrBlockSize = lastClrBlock.m_NumBlocks * lastClrBlock.m_BytesPerBlock;
                        var flashClrEndAddress = lastClrBlockStartAddress + lastClrBlockSize;

                        // setup array to receive binary output
                        byte[] flashDumpBuffer = new byte[flashClrEndAddress - flashStartAddress];

                        // dump flash
                        var dumpFlashOperation = device.DebugEngine.ReadMemory(flashStartAddress, (uint)flashDumpBuffer.Length);
                        if (dumpFlashOperation.Success)
                        {
                            // copy to array
                            Array.Copy(dumpFlashOperation.Buffer, flashDumpBuffer, flashDumpBuffer.Length);
                        }
                        else
                        {
                            // operation failed

                            MessageCentre.InternalErrorMessage($"Failed to dump flash. Error code: { dumpFlashOperation.ErrorCode}.");
                            await outputPaneWriter.WriteLineAsync($"Failed to dump flash. Error code: { dumpFlashOperation.ErrorCode}.");

                            // better clean dump cache file name
                            targetFlashDumpFileName = null;
                        }

                        // store dump in cache file
                        using (FileStream binFile = new FileStream(targetFlashDumpFileName, FileMode.Create))
                        {
                            await binFile.WriteAsync(flashDumpBuffer, 0, flashDumpBuffer.Length);
                        }

                        // done here
                        MessageCentre.InternalErrorMessage($@"Flash dump stored @ ""{ targetFlashDumpFileName }"".");
                        await outputPaneWriter.WriteLineAsync($@"Flash dump stored @ ""{ targetFlashDumpFileName }"".");
                    }
                    else
                    {
                        // we already have a dump on cache, so we are good to go
                        MessageCentre.InternalErrorMessage($@"Found flash dump on cache @ ""{ targetFlashDumpFileName }"".");
                        await outputPaneWriter.WriteLineAsync($@"Found flash dump on cache ""{ targetFlashDumpFileName }"".");
                    }
                }
                catch(Exception ex)
                {
                    MessageCentre.InternalErrorMessage($"Exception when setting up flash dump: ({ ex.Message + Environment.NewLine + ex.StackTrace })");
                    await outputPaneWriter.WriteLineAsync("Warning: exception when setting up flash dump. Skipping this step.");

                    // can't make any decisions on what went wrong so clear the file name so it doesn't affect the next steps
                    targetFlashDumpFileName = null;
                }
            }
            else
            {
                // platforms other than STM32 don't seem to benefit from this feature
                MessageCentre.InternalErrorMessage("nanoDevice is not STM32, skipping flash dump.");
                await outputPaneWriter.WriteLineAsync("Generating a deployment image for this nanoDevice is not supported.");
            }

            return targetFlashDumpFileName;
        }

        /// <summary>
        /// Generate deployment image.
        /// </summary>
        /// <param name="device">nanoDevice being deployed to.</param>
        /// <param name="flashDumpFileName">A string with the path for the flash dump file.</param>
        /// <param name="assemblies">List of PE assemblies to be added to the deployment region</param>
        /// <param name="configuredProject">The <see cref="ConfiguredProject"/> object that is being deployed.</param>
        /// <param name="outputPaneWriter">The output pane writer where to output progress messages.</param>
        internal static async Task GenerateDeploymentImageAsync(
            NanoDeviceBase device, 
            string flashDumpFileName, 
            List<byte[]> assemblies,
            ConfiguredProject configuredProject,
            TextWriter outputPaneWriter)
        {
            // sanity check
            if(string.IsNullOrEmpty(flashDumpFileName))
            {
                // no flash dump file, can't proceed
                MessageCentre.InternalErrorMessage("No flash dump file provided, can't generate deployment image.");
                return;
            }

            await Task.Yield();

            // This can go wrong in many ways.
            // Because it's a subsidiary operation of "deploy" and, most important, it's not part of the standard "deploy" VS concept,
            // it's wrapped on a try catch block so the deployment can proceed without it.
            try
            {
                await outputPaneWriter.WriteLineAsync("Generating deployment image.");

                // get memory map
                var memoryMap = device.DebugEngine.GetMemoryMap();

                // get flash map
                var flashSectorMap = device.DebugEngine.GetFlashSectorMap();

                // get flash start address
                var flashStartAddress = memoryMap.First(m => (m.m_flags & Commands.Monitor_MemoryMap.c_FLASH) == Commands.Monitor_MemoryMap.c_FLASH).m_address;

                // get config block addresses
                var configBlock = flashSectorMap.Last(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG);
                var configBlockStartAddress = configBlock.m_StartAddress;
                var configBlockSize = configBlock.m_NumBlocks * configBlock.m_BytesPerBlock;

                // get end address of CLR region
                var lastClrBlock = flashSectorMap.Last(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE);
                var lastClrBlockStartAddress = lastClrBlock.m_StartAddress;
                var lastClrBlockSize = lastClrBlock.m_NumBlocks * lastClrBlock.m_BytesPerBlock;
                var flashClrEndAddress = lastClrBlockStartAddress + lastClrBlockSize;

                // get deployment start address
                var deploymentStartAddress = flashSectorMap.Last(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT).m_StartAddress;

                // read flash dump file
                byte[] flashDumpBuffer;

                using (FileStream fs = File.Open(flashDumpFileName, FileMode.Open, FileAccess.Read))
                {
                    flashDumpBuffer = new byte[fs.Length];
                    await fs.ReadAsync(flashDumpBuffer, 0, (int)fs.Length);
                }

                if (!NanoFrameworkPackage.SettingIncludeConfigBlockInDeploymentImage)
                {
                    // config block is not to be included in the deployment image
                    // clear it from flash dump buffer
                    // compute offset of config block from flash start address
                    for (uint i = (configBlockStartAddress - flashStartAddress); i < configBlockSize; i++)
                    {
                        // set it to 0xFF which is flash erased value
                        flashDumpBuffer[i] = 0xFF;
                    }
                }

                // now check if there is any empty space between CLR last address and deployment start address
                if (deploymentStartAddress > flashClrEndAddress)
                {
                    // add extra space to the flash dump buffer
                    Array.Resize(ref flashDumpBuffer, (int)(flashDumpBuffer.Length + (deploymentStartAddress - flashClrEndAddress)));

                    // fill with 0xFF which is flash erased value
                    for (uint i = (deploymentStartAddress - flashClrEndAddress); i < (deploymentStartAddress - flashClrEndAddress); i++)
                    {
                        flashDumpBuffer[i] = 0xFF;
                    }
                }

                var projectOutputPath = await GetProjectOutputPathAsync(configuredProject);

                // build cache file name
                var generatedImageFileName = Path.Combine(projectOutputPath, "deployment-image.bin");

                MessageCentre.InternalErrorMessage("Writing generated image file now.");

                // write deployment image file
                using (FileStream binFile = new FileStream(generatedImageFileName, FileMode.Create))
                {
                    await binFile.WriteAsync(flashDumpBuffer, 0, flashDumpBuffer.Length);

                    foreach (byte[] pe in assemblies)
                    {
                        await binFile.WriteAsync(pe, 0, pe.Length);
                    }
                }

                MessageCentre.InternalErrorMessage($@"Deployment image generated saved @ ""{ generatedImageFileName }"".");
                await outputPaneWriter.WriteLineAsync($@"Deployment image generated saved @ ""{ generatedImageFileName }"".");
            }
            catch (Exception ex)
            {
                MessageCentre.InternalErrorMessage($"Exception when generating deployment image: ({ ex.Message + Environment.NewLine + ex.StackTrace })");
                await outputPaneWriter.WriteLineAsync("Warning: exception when generating deployment image. Skipping this step.");
            }
        }

        private static async Task<string> GetProjectOutputPathAsync(ConfiguredProject configuredProject)
        {
            // need to access the target path using reflection (step by step)
            // get type for ConfiguredProject
            var projSystemType = configuredProject.GetType();

            // get private property MSBuildProject
            var buildProject = projSystemType.GetTypeInfo().GetDeclaredProperty("MSBuildProject");

            // get value of MSBuildProject property from ConfiguredProject object
            // this result is of type Microsoft.Build.Evaluation.Project
            var projectResult = await((Task<Microsoft.Build.Evaluation.Project>)buildProject.GetValue(configuredProject));

            // we want the target path property
            return Path.GetDirectoryName(projectResult.Properties.First(p => p.Name == "TargetPath").EvaluatedValue);
        }
    }
}
