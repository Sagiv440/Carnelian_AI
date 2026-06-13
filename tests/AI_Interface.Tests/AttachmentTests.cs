using AI_Interface.Models;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>Unit tests for <see cref="Attachment.KindForPath"/> — the pure image-vs-document extension check
/// used when attaching a file from the project tree.</summary>
public sealed class AttachmentTests
{
    [Theory]
    [InlineData("photo.png", AttachmentKind.Photo)]
    [InlineData("PHOTO.JPG", AttachmentKind.Photo)]      // case-insensitive
    [InlineData("scan.jpeg", AttachmentKind.Photo)]
    [InlineData("anim.gif", AttachmentKind.Photo)]
    [InlineData("pic.webp", AttachmentKind.Photo)]
    [InlineData("img.bmp", AttachmentKind.Photo)]
    [InlineData("notes.txt", AttachmentKind.Document)]
    [InlineData("report.pdf", AttachmentKind.Document)]
    [InlineData("Program.cs", AttachmentKind.Document)]
    [InlineData("README", AttachmentKind.Document)]      // no extension
    [InlineData("archive.tar.gz", AttachmentKind.Document)]
    public void KindForPath_PicksPhotoForImages_ElseDocument(string path, AttachmentKind expected)
    {
        Assert.Equal(expected, Attachment.KindForPath(path));
    }

    [Fact]
    public void KindForPath_HandlesFullPaths()
    {
        Assert.Equal(AttachmentKind.Photo, Attachment.KindForPath(@"C:\projects\demo\assets\logo.PNG"));
        Assert.Equal(AttachmentKind.Document, Attachment.KindForPath("/home/user/project/main.py"));
    }
}
