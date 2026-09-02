using System.IO;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Central place for experiment output paths. Paths are computed from the
    /// current project/build location so the project can be copied to another
    /// PC without editing absolute user-specific directories.
    /// </summary>
    public static class ResearchDataPaths
    {
        public const string DataRootFolderName = "ExperimentData";
        public const string UxfFolderName = "DrivingTempoStability_UXF";
        public const string RelativeUxfDataRoot = DataRootFolderName + "/" + UxfFolderName;

        public static string ProjectRoot
        {
            get
            {
                DirectoryInfo assetsParent = Directory.GetParent(Application.dataPath);
                if (assetsParent != null)
                    return assetsParent.FullName;

                return Application.persistentDataPath;
            }
        }

        public static string UxfDataRoot
        {
            get { return Path.Combine(ProjectRoot, DataRootFolderName, UxfFolderName); }
        }

        public static string LegacyTelemetryRoot
        {
            get { return Path.Combine(ProjectRoot, DataRootFolderName, "LegacyTelemetry"); }
        }

        public static string EnsureUxfDataRoot()
        {
            Directory.CreateDirectory(UxfDataRoot);
            return UxfDataRoot;
        }
    }
}
