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

        Assert.Equal(20_000, chunkedList.Count);
    }

    [Fact]
    public void GivenAnItemAddedIncreaseCount()
    {
        var testItem = new TestData("First Item", 1);
        ChunkedList<TestData> chunkedList = [];

        chunkedList.Add(testItem);

        Assert.Single(chunkedList);
    }

    [Fact]
    public void EnumeratorReturnsValuesCorrectly()
    {
        var list = new ChunkedList<int>(4)
        {
            1,
            2
        };

        using var enumerator = list.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(1, enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(2, enumerator.Current);
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void CanClearList()
    {
        var list = new ChunkedList<int>(4)
        {
            1,
            2
        };

        list.Clear();

        Assert.Empty(list);
        Assert.True(list.Count == 0);
    }

    [Fact]
    public void DefaultSortOrdersCorrectly()
    {
        var list = new ChunkedList<int>(4)
        {
            4,
            5,
            1,
            8
        };

        list.Sort();

        Assert.Equal(1, list[0]);
        Assert.Equal(4, list[1]);
        Assert.Equal(5, list[2]);
        Assert.Equal(8, list[3]);
    }

    [Fact]
    public void RandomizedNumbersSortedCorrectly()
    {
        const int initialCount = 100_000;
        const int chunkByteSize = 5;
        var random = new Random();
        for(var count = initialCount; count < initialCount + (1 << chunkByteSize); count++)
        {
            var list = new ChunkedList<int>(5);
            for(var i = 0; i < count; i++)
                list.Add(random.Next(0, 10_000));
            
            list.Sort();

            for(var i = 1; i < list.Count; i++)
                Assert.True(list[i-1] <= list[i]);
        }
    }

    private sealed record TestData(string Name, int Value);
}
