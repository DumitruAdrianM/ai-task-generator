namespace AiTaskGenerator
{
    public class FileWriterService
    {
        // Files in the same group resolve to the same module name in webpack/node.
        // If we write App.jsx while App.js exists, the .js shadows the .jsx and the
        // new file is silently ignored. We delete conflicting siblings before writing.
        private static readonly string[][] ConflictGroups =
        {
            new[] { ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs" },
        };

        public void WriteFiles(string repoPath, List<GeneratedFile> files)
        {
            if (files == null) return;

            foreach (var file in files)
            {
                var normalized = NormalizePath(file.Path, repoPath);
                var fullPath = Path.Combine(repoPath, normalized);

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                RemoveConflictingExtensions(fullPath);

                File.WriteAllText(fullPath, file.Content);

                // Mutate the GeneratedFile so downstream consumers (QaService) see
                // the normalized relative path too.
                file.Path = normalized;
            }
        }

        // Strip an accidental repo-prefix so paths stay relative. Accepts:
        //   "src/App.js"                                  -> unchanged
        //   "D:\Proiecte\...\ai-react-app\src\App.js"     -> "src/App.js"
        //   "/src/App.js"                                 -> "src/App.js"
        public static string NormalizePath(string path, string repoPath)
        {
            if (string.IsNullOrEmpty(path)) return path;

            var p = path.Replace('\\', '/').TrimStart('/');

            var repoNorm = repoPath.Replace('\\', '/').TrimEnd('/');
            // strip "D:/Proiecte/.../ai-react-app/" prefix if present
            if (p.StartsWith(repoNorm + "/", StringComparison.OrdinalIgnoreCase))
                p = p.Substring(repoNorm.Length + 1);
            else if (Path.IsPathRooted(p))
            {
                // absolute path but NOT inside the repo — fall back to just the
                // tail after the repo's folder name if we can find it.
                var repoFolderName = Path.GetFileName(repoNorm);
                var idx = p.IndexOf("/" + repoFolderName + "/",
                    StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    p = p.Substring(idx + repoFolderName.Length + 2);
            }

            return p;
        }

        private void RemoveConflictingExtensions(string fullPath)
        {
            var ext = Path.GetExtension(fullPath);
            if (string.IsNullOrEmpty(ext)) return;

            var withoutExt = Path.ChangeExtension(fullPath, null);

            foreach (var group in ConflictGroups)
            {
                if (!group.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    continue;

                foreach (var otherExt in group)
                {
                    if (string.Equals(otherExt, ext, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var alt = withoutExt + otherExt;
                    try
                    {
                        if (File.Exists(alt))
                        {
                            File.Delete(alt);
                            Console.WriteLine(
                                $"Removed shadowing file {alt} before writing {fullPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"Warning: could not delete conflicting {alt}: {ex.Message}");
                    }
                }
            }
        }
    }
}
