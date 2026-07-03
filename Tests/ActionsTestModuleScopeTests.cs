using System;
using System.IO;
using System.Linq;
using Xunit;

namespace RimMind.Actions.Tests
{
    /// <summary>
    /// Guards against re-introducing Core-domain test files in the Actions test project.
    /// Core types (Result, RimMindError, RimMindErrorCode, MechanismRisk, RiskLevel,
    /// StructuredTool, TraceContext) must be tested in RimMind-Core/Tests/Result/, not here.
    /// </summary>
    public class ActionsTestModuleScopeTests
    {
        private static readonly string TestsDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        // File names whose presence outside _backup/ indicates a Core-type test leak.
        private static readonly string[] BannedTestFiles =
        {
            "ActionResultTests.cs",
            "ResultAdvancedTests.cs",
            "RimMindErrorTests.cs",
            "TraceContextTests.cs",
            "MechanismRiskTests.cs",
            "RiskLevelTests.cs",
            "RimMindErrorCodeTests.cs",
            "StructuredToolTests.cs",
        };

        [Fact]
        public void Actions_Tests_Do_Not_Contain_Core_Domain_Test_Files()
        {
            var testsRoot = Path.Combine(TestsDir, "RimMind-Actions", "Tests");
            Assert.True(Directory.Exists(testsRoot), $"Tests directory missing: {testsRoot}");

            var offending = Directory.GetFiles(testsRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => BannedTestFiles.Contains(name))
                .ToList();

            Assert.True(offending.Count == 0,
                $"Core-domain test files found in Actions test project (move to RimMind-Core/Tests/Result/ or _backup/): {string.Join(", ", offending)}");
        }

        [Fact]
        public void Actions_Tests_Backup_Directory_Exists()
        {
            var backupDir = Path.Combine(TestsDir, "RimMind-Actions", "Tests", "_backup");
            Assert.True(Directory.Exists(backupDir),
                $"_backup directory missing: {backupDir}. It must hold migrated/removed test files.");
        }
    }
}
