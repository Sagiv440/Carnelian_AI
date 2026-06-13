using System.Runtime.CompilerServices;

// Expose internal helpers (e.g. OllamaInstaller's pure OS-decision helpers) to the test project so each
// OS branch can be asserted on any host without OperatingSystem.Is* checks.
[assembly: InternalsVisibleTo("Carnelian.Tests")]
