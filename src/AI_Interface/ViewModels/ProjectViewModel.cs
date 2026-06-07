using System.IO;
using AI_Interface.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>
/// Backs the Project window. "New Project" takes a name + parent location and creates a folder named
/// after the project (with a <c>.AI</c> folder inside); "Open Project" takes an existing folder and
/// derives the project name from it. The window closes with the built <see cref="Project"/> (see
/// ProjectWindow code-behind), so there are no commands here.
/// </summary>
public sealed partial class ProjectViewModel : ViewModelBase
{
    // --- New Project tab ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(NewProjectDirectory))]
    private string _projectName = "";

    /// <summary>Parent location chosen by the user; the project folder is created inside it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(NewProjectDirectory))]
    private string _directory = "";

    /// <summary>Both fields must be filled before a project can be created.</summary>
    public bool CanCreate =>
        !string.IsNullOrWhiteSpace(ProjectName) && !string.IsNullOrWhiteSpace(Directory);

    /// <summary>The folder that will be created: a sub-folder named after the project inside the location.</summary>
    public string NewProjectDirectory =>
        CanCreate ? Path.Combine(Directory.Trim(), ProjectName.Trim()) : "";

    public Project Build() => new(ProjectName.Trim(), NewProjectDirectory);

    // --- Open Project tab ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(OpenProjectName))]
    private string _openDirectory = "";

    /// <summary>Project name for the Open tab: the chosen folder's own name.</summary>
    public string OpenProjectName => FolderName(OpenDirectory);

    public bool CanOpen => !string.IsNullOrWhiteSpace(OpenDirectory);

    public Project BuildOpen() => new(OpenProjectName, OpenDirectory.Trim());

    /// <summary>The last path segment (folder name), falling back to the whole path for drive roots.</summary>
    private static string FolderName(string? path)
    {
        var dir = path?.Trim();
        if (string.IsNullOrEmpty(dir))
            return "";
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? dir : name;
    }
}
