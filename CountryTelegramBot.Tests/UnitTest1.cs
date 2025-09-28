using Xunit;

namespace CountryTelegramBot.Tests;

public class UnitTest1
{
    [Fact]
    public void Test_XUnit_Framework_IsWorking()
    {
        // Arrange
        var expected = "Hello, xUnit!";
        
        // Act
        var actual = "Hello, xUnit!";
        
        // Assert
        Assert.Equal(expected, actual);
    }
    
    [Fact]
    public void Test_MainProject_Reference_IsWorking()
    {
        // Arrange & Act
        var timeHelper = new TimeHelper(null);
        
        // Assert
        Assert.NotNull(timeHelper);
    }
}
