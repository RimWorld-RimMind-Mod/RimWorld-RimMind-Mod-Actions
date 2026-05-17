using RimMind.Domain.ValueObjects;
using Xunit;

namespace RimMind.Actions.Tests
{
    public class ActionResultTests
    {
        [Fact]
        public void Result_Ok_IndicatesSuccess()
        {
            var result = Result<string, RimMindError>.Ok("action completed");
            Assert.True(result.IsOk);
            Assert.Equal("action completed", result.Value);
        }

        [Fact]
        public void Result_Err_IndicatesFailure()
        {
            var error = new RimMindError(RimMindErrorCode.MechanismPawnNotFound, "Pawn not found");
            var result = Result<string, RimMindError>.Err(error);
            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.MechanismPawnNotFound, result.Error.Code);
        }

        [Fact]
        public void Result_Bool_Ok_IsTrue()
        {
            var result = Result<bool, RimMindError>.Ok(true);
            Assert.True(result.IsOk);
            Assert.True(result.Value);
        }

        [Fact]
        public void Result_Bool_Err_IsNotSuccess()
        {
            var error = new RimMindError(RimMindErrorCode.MechanismPawnNotFound, "Pawn not found");
            var result = Result<bool, RimMindError>.Err(error);
            Assert.True(result.IsErr);
        }
    }
}
