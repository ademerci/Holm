namespace WinNotch.Tests;

public class ShelfTests
{
    static bool All(string _) => true;

    [Fact]
    public void Adds_what_exists_once()
    {
        var shelf = new List<string> { @"C:\a.txt" };
        int added = Shelf.Add(shelf, new[] { @"C:\A.TXT", @"C:\b.txt", @"C:\gone.txt", " " }, p => p != @"C:\gone.txt");
        Assert.Equal(1, added);
        Assert.Equal(new[] { @"C:\a.txt", @"C:\b.txt" }, shelf);
    }

    [Fact]
    public void The_oldest_make_room()
    {
        var shelf = Enumerable.Range(0, Shelf.Max).Select(i => $@"C:\{i}.txt").ToList();
        Shelf.Add(shelf, new[] { @"C:\new1.txt", @"C:\new2.txt" }, All);
        Assert.Equal(Shelf.Max, shelf.Count);
        Assert.Equal(@"C:\2.txt", shelf[0]);
        Assert.Equal(@"C:\new2.txt", shelf[^1]);
    }

    [Fact]
    public void Removes_by_path_whatever_the_case()
    {
        var shelf = new List<string> { @"C:\a.txt", @"C:\b.txt" };
        Assert.Equal(1, Shelf.Remove(shelf, new[] { @"c:\A.txt", @"C:\other.txt" }));
        Assert.Equal(new[] { @"C:\b.txt" }, shelf);
    }

    [Fact]
    public void Forgets_files_that_are_gone()
    {
        using var folder = new TempFolder();
        string kept = Path.Combine(folder.Path, "kept.txt");
        File.WriteAllText(kept, "x");
        var shelf = new List<string> { kept, Path.Combine(folder.Path, "moved.txt"), folder.Path };
        Assert.Equal(1, Shelf.Prune(shelf));
        Assert.Equal(new[] { kept, folder.Path }, shelf);
    }

    [Theory]
    [InlineData(@"C:\Users\me\Report.pdf", "Report.pdf")]
    [InlineData(@"C:\Users\me\Photos\", "Photos")]
    [InlineData(@"D:\", @"D:\")]
    public void Shows_the_name(string path, string expected) => Assert.Equal(expected, Shelf.DisplayName(path));
}
