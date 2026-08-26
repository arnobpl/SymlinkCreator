namespace SymlinkCreator.Tests;

[TestClass]
public sealed class BatchScriptSyntaxTests
{
    [TestMethod]
    public void TryQuoteEscapesPercentSignsAndPreservesSpaces()
    {
        bool result = BatchScriptSyntax.TryQuote(
            "C:\\Path With Space\\100%.txt",
            out string quotedValue);

        Assert.IsTrue(result);
        Assert.AreEqual("\"C:\\Path With Space\\100%%.txt\"", quotedValue);
    }

    [TestMethod]
    [DataRow("path\"with-quote")]
    [DataRow("path\rwith-newline")]
    [DataRow("path\nwith-newline")]
    public void TryQuoteRejectsCharactersThatWouldBreakBatchSyntax(string value)
    {
        bool result = BatchScriptSyntax.TryQuote(value, out string quotedValue);

        Assert.IsFalse(result);
        Assert.AreEqual(string.Empty, quotedValue);
    }

    [TestMethod]
    public void TryQuoteRejectsNull()
    {
        bool result = BatchScriptSyntax.TryQuote(null, out string quotedValue);

        Assert.IsFalse(result);
        Assert.AreEqual(string.Empty, quotedValue);
    }
}
