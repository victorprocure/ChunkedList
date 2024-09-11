namespace ChunkedList.Tests;

public class ChunkedListTests
{
    [Fact]
    public void GivenValidDataCanAdd()
    {
        var testItem = new TestData("First Item", 1);
        ChunkedList<TestData> chunkedList = [testItem];

        Assert.Contains(testItem, chunkedList);
    }

    [Fact]
    public void GivenValidDataCanRemove()
    {
        var testItem = new TestData("First Item", 1);
        var testItem2 = new TestData("Second Item", 2);
        ChunkedList<TestData> chunkedList = [testItem, testItem2];

        chunkedList.Remove(testItem2);

        Assert.Contains(testItem, chunkedList);
        Assert.DoesNotContain(testItem2, chunkedList);
        Assert.True(chunkedList.Count == 1);
    }

    [Fact]
    public void GivenValidDataCanRemoveFirstItemAndRebalance()
    {
        var testItem = new TestData("First Item", 1);
        var testItem2 = new TestData("Second Item", 2);
        ChunkedList<TestData> chunkedList = [testItem, testItem2];

        chunkedList.Remove(testItem);

        Assert.Contains(testItem2, chunkedList);
        Assert.DoesNotContain(testItem, chunkedList);
        Assert.True(chunkedList[0] == testItem2);
        Assert.True(chunkedList.Count == 1);
    }

    [Fact]
    public void GivenValidDataCanInsertAndRebalance()
    {
        var testItem = new TestData("First Item", 1);
        var testItem2 = new TestData("Second Item", 2);
        var testItem3 = new TestData("Third Item", 3);
        var testItem4 = new TestData("Fourth Item", 4);
        ChunkedList<TestData> chunkedList = [testItem, testItem2, testItem4];

        chunkedList.Insert(2, testItem3);

        Assert.Contains(testItem, chunkedList);
        Assert.Contains(testItem2, chunkedList);
        Assert.Contains(testItem3, chunkedList);
        Assert.Contains(testItem4, chunkedList);
        Assert.True(chunkedList[2] == testItem3);
        Assert.True(chunkedList[3] == testItem4);
        Assert.True(chunkedList.Count == 4);
    }

    [Fact]
    public void CanHandleManyItems()
    {
        ChunkedList<TestData> chunkedList = [];
        for(var i = 0; i < 20_000; i++)
        {
            chunkedList.Add(new TestData("Name", i));
        }

        Assert.True(chunkedList.Count == 20_000);
    }


    private sealed record TestData(string Name, int Value);
}
