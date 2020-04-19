﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BlazorLazyLoading.ManifestGenerators;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;

namespace BlazorLazyLoading
{
    public class GenerateManifest : Task
    {
        private readonly ICollection<IManifestGenerator> _manifestGenerators;

        [Required]
        public string[] AssemblyNames { get; set; } = Array.Empty<string>();

        [Required]
        public string[] AssemblyPaths { get; set; } = Array.Empty<string>();

        [Required]
        public string ManifestOutputPath { get; set; } = string.Empty;

        public GenerateManifest()
        {
            _manifestGenerators = new IManifestGenerator[]
            {
                new ComponentManifestGenerator(),
                new RouteManifestGenerator(),
            };
        }

        public override bool Execute()
        {
            SanitizeInput();

            var manifest = new Dictionary<string, IDictionary>();

            using var dllMetadataContext = CreateDllMetadataContext(ResolveAvailableDlls());

            foreach (string assemblyName in AssemblyNames)
            {
                Assembly assembly = dllMetadataContext.LoadFromAssemblyName(assemblyName);
                Dictionary<string, object> manifestSections = ExecuteManifestGenerators(assembly);
                manifest.Add(assemblyName, manifestSections);

                var manifestDescriptions = manifestSections.Select(s =>
                    "'" + s.Key + "'" + (s.Value is ICollection c ? ": " + c.Count : string.Empty) + "");

                Info($"Lazy Module '{assemblyName}' generated with: {{ {string.Join(", ", manifestDescriptions)} }}");
            }

            string manifestJson = JsonConvert.SerializeObject(manifest);
            File.WriteAllText(ManifestOutputPath, manifestJson);

            return true;
        }

        private void SanitizeInput()
        {
            AssemblyPaths = AssemblyPaths.Select(p => p
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar))
                .Distinct()
                .ToArray();

            AssemblyNames = AssemblyNames
                .Distinct()
                .ToArray();
        }

        private IEnumerable<string> ResolveAvailableDlls()
        {
            var coreDlls = new[] { typeof(object).Assembly.Location };
            var runtimeDlls = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
            var moduleDlls = AssemblyPaths.SelectMany(p => Directory.GetFiles(p, "*.dll"));

            return coreDlls
                .Concat(runtimeDlls)
                .Concat(moduleDlls)
                .ToList();
        }

        private MetadataLoadContext CreateDllMetadataContext(IEnumerable<string> dlls)
        {
            var resolver = new PathAssemblyResolver(dlls);
            return new MetadataLoadContext(resolver);
        }

        private Dictionary<string, object> ExecuteManifestGenerators(Assembly assembly)
        {
            var manifestSections = new Dictionary<string, object>();

            foreach (var manifestGenerator in _manifestGenerators)
            {
                var manifestSection = manifestGenerator.GenerateManifest(assembly);

                foreach (var keyValue in manifestSection)
                {
                    if (manifestSections.ContainsKey(keyValue.Key))
                    {
                        throw new NotSupportedException("Duplicated manifest section keys");
                    }

                    manifestSections.Add(keyValue.Key, keyValue.Value);
                }
            }

            return manifestSections;
        }

        private void Info(string message, params object[] args)
        {
            Log.LogMessage(MessageImportance.High, message, args);
        }
    }
}
