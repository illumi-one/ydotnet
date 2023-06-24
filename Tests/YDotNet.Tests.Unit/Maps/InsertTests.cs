using NUnit.Framework;
using YDotNet.Document;
using YDotNet.Document.Cells;
using YDotNet.Document.Transactions;
using YDotNet.Document.Types;

namespace YDotNet.Tests.Unit.Maps;

public class InsertTests
{
    [Test]
    [Ignore("To be implemented.")]
    public void InsertBoolean()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertDouble()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertLong()
    {
    }

    [Test]
    public void InsertString()
    {
        // Arrange
        var (map, transaction) = ArrangeDoc();
        var name = "Lucas";

        // Assert
        Assert.That(map.Length(transaction), Is.EqualTo(expected: 0));

        // Act
        map.Insert(transaction, "name", new Input(name));

        // Assert
        Assert.That(map.Length(transaction), Is.EqualTo(expected: 1));
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertBytes()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertCollection()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertObject()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertNull()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertUndefined()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertText()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertArray()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertMap()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertXmlElement()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertXmlText()
    {
    }

    [Test]
    public void InsertDoc()
    {
        // Arrange
        var (map, transaction) = ArrangeDoc();
        var subDoc = new Doc();

        // Assert
        Assert.That(map.Length(transaction), Is.EqualTo(expected: 0));

        // Act
        map.Insert(transaction, "sub-doc", new Input(subDoc));

        // Assert
        Assert.That(map.Length(transaction), Is.EqualTo(expected: 1));
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertEqualTypeOnExistingKey()
    {
    }

    [Test]
    [Ignore("To be implemented.")]
    public void InsertDifferentTypeOnExistingKey()
    {
    }

    private (Map?, Transaction?) ArrangeDoc()
    {
        var doc = new Doc();
        var map = doc.Map("map");
        var transaction = doc.WriteTransaction();

        return (map, transaction);
    }
}
