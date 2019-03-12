using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymlinkCreator.core;

namespace SymlinkCreatorTests
{
    [TestClass]
    public class SymlinkUtilityTest
    {
        [TestMethod]
        public void GetRelativePath_Test()
        {
            SymlinkAgent symlinkAgent = new SymlinkAgent(new List<string>(), string.Empty, true);
            PrivateObject obj = new PrivateObject(symlinkAgent);

            // Test single-backward target file path
            var retVal = obj.Invoke("GetRelativePath", "D:\\Abc\\Def\\Ghi", "D:\\Abc\\Def\\Qrs\\Test.mp3");
            Assert.AreEqual("..\\Qrs\\Test.mp3", retVal);

            // Test multiple-backward target file path
            retVal = obj.Invoke("GetRelativePath", "D:\\Abc\\Def\\Ghi\\Jkl\\Mno", "D:\\Abc\\Def\\Qrs\\Test.mp3");
            Assert.AreEqual("..\\..\\..\\Qrs\\Test.mp3", retVal);

            // Test single-forward target file path
            retVal = obj.Invoke("GetRelativePath", "D:\\Abc\\Def\\Ghi", "D:\\Abc\\Def\\Ghi\\Jkl\\Test.mp3");
            Assert.AreEqual("Jkl\\Test.mp3", retVal);

            // Test multiple-forward target file path
            retVal = obj.Invoke("GetRelativePath", "D:\\Abc\\Def\\Ghi", "D:\\Abc\\Def\\Ghi\\Jkl\\Mno\\Test.mp3");
            Assert.AreEqual("Jkl\\Mno\\Test.mp3", retVal);

            // Test current-directory target file path
            retVal = obj.Invoke("GetRelativePath", "D:\\Abc\\Def\\Ghi", "D:\\Abc\\Def\\Ghi\\Test.mp3");
            Assert.AreEqual("Test.mp3", retVal);
        }
    }
}