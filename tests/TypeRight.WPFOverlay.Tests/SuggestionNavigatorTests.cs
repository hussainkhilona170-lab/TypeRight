using TypeRight.WPFOverlay;
using Xunit;

namespace TypeRight.WPFOverlay.Tests
{
    public class SuggestionNavigatorTests
    {
        [Fact]
        public void MoveNext_WrapsAtEnd()
        {
            Assert.Equal(1, SuggestionNavigator.MoveNext(0, 3));
            Assert.Equal(2, SuggestionNavigator.MoveNext(1, 3));
            Assert.Equal(0, SuggestionNavigator.MoveNext(2, 3));
        }

        [Fact]
        public void MovePrevious_WrapsAtStart()
        {
            Assert.Equal(2, SuggestionNavigator.MovePrevious(0, 3));
            Assert.Equal(1, SuggestionNavigator.MovePrevious(2, 3));
            Assert.Equal(0, SuggestionNavigator.MovePrevious(1, 3));
        }

        [Fact]
        public void Navigation_EmptyListReturnsMinusOne()
        {
            Assert.Equal(-1, SuggestionNavigator.MoveNext(0, 0));
            Assert.Equal(-1, SuggestionNavigator.MovePrevious(0, 0));
        }
    }
}
