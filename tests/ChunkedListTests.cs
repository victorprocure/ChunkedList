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
