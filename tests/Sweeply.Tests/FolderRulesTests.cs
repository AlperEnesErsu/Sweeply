using Sweeply.Services;

namespace Sweeply.Tests;

public class FolderRulesTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly string _home;
    readonly string _documents;
    readonly string _system;

    public FolderRulesTests()
    {
        // Kök: _dir. Profil: _dir\home. Kişisel: home\Documents. Sistem: _dir\sys\Windows.
        _home = _dir.Dir("home");
        _documents = _dir.Dir(@"home\Documents");
        _system = _dir.Dir(@"sys\Windows");
    }

    public void Dispose() => _dir.Dispose();

    string? Check(string path) => FolderRules.Validate(path, _home, [_documents], [_system]);

    [Fact]
    public void FolderInsideProfile_IsAllowed() =>
        Assert.Null(Check(_dir.Dir(@"home\AppData\Local\Cache")));

    [Fact]
    public void ProfileItself_IsRejected() =>
        Assert.NotNull(Check(_home));

    [Fact]
    public void AncestorOfProfile_IsRejected() =>
        Assert.NotNull(Check(_dir.Path));

    [Fact]
    public void PersonalFolderAndItsChildren_AreRejected()
    {
        Assert.NotNull(Check(_documents));
        Assert.NotNull(Check(_dir.Dir(@"home\Documents\proje")));
    }

    [Fact]
    public void SystemFolder_IsRejected() =>
        Assert.NotNull(Check(_system));

    [Fact]
    public void MissingFolder_IsRejected() =>
        Assert.NotNull(Check(Path.Combine(_home, "yok")));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPath_IsRejected(string path) =>
        Assert.NotNull(Check(path));

    [Fact]
    public void Overlaps_DetectsAncestorDescendantAndEqual_ButNotSiblingPrefix()
    {
        Assert.True(FileCleaner.Overlaps(@"C:\a\b", @"C:\a\b"));
        Assert.True(FileCleaner.Overlaps(@"C:\a", @"C:\a\b"));
        Assert.True(FileCleaner.Overlaps(@"C:\a\b\c", @"C:\a\b"));
        Assert.False(FileCleaner.Overlaps(@"C:\a\bc", @"C:\a\b"));
    }
}
