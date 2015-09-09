using System;
using System.Linq;
using NUnit.Framework;
using StackExchange.Redis;

namespace RedisStore
{
    public interface IUser
    {
        int Id { get; set; }
        string Name { get; set; }
        bool IsAwesome { get; set; }
        bool? IsAwesomer { get; set; }
        long Score { get; set; }
        long? Pointses { get; set; }
    }

    interface INoIdProperty { }

    class SomeClass { }

    class SomeOtherClass { }

    interface IInvalidPropertyTypes
    {
        int Id { get; set; }
        SomeClass SomeClass { get; set; }
        SomeOtherClass SomeOtherClass{ get; set; }
    }

    [TestFixture]
    public class Tests
    {
        static Store GetStore()
        {
            var redis = ConnectionMultiplexer.Connect("localhost:6379");
            return new Store(redis);
        }

        [Test]
        [ExpectedException(typeof(NotAnInterfaceException))]
        public void NotAnInterfaceThrows()
        {
            var s = GetStore();
            var notAnInterface = s.Create<string>();
        }

        [Test]
        [ExpectedException(typeof (NoIdPropertyException))]
        public void NoIdPropertyThrows()
        {
            var s = GetStore();
            var iNoId = s.Create<INoIdProperty>();
        }

        [Test]
        public void InvalidPropertyTypesThrows()
        {
            try
            {
                var s = GetStore();
                var badProperties = s.Create<IInvalidPropertyTypes>();
                Assert.Fail();
            }
            catch (AggregateException ex)
            {
                try
                {
                    ex.InnerExceptions.Single(o => o.Message == "IInvalidPropertyTypes.SomeClass has an invalid type. Valid types are: Boolean,Boolean?,Byte[],Double,Double?,Int32,Int32?,Int64,Int64?,String");
                    ex.InnerExceptions.Single(o => o.Message == "IInvalidPropertyTypes.SomeOtherClass has an invalid type. Valid types are: Boolean,Boolean?,Byte[],Double,Double?,Int32,Int32?,Int64,Int64?,String");
                }
                catch (InvalidOperationException)
                {
                    foreach (var innerEx in ex.InnerExceptions)
                    {
                        Console.WriteLine(innerEx.Message);
                    }
                    throw ex;
                }
            }
        }

        [Test]
        public void WritesAndReadsWork()
        {
            var s = GetStore();
            var user = s.Create<IUser>();

            user.Id = 17;
            Assert.AreEqual(17, user.Id);

            user.Name = "Bob Bobberson";
            Assert.AreEqual("Bob Bobberson", user.Name);

            user.IsAwesome = true;
            Assert.AreEqual(true, user.IsAwesome);

            user.IsAwesomer = null;
            Assert.AreEqual(null, user.IsAwesomer);

            user.Score = 1000L;
            Assert.AreEqual(1000L, user.Score);

            user.Pointses = null;
            Assert.AreEqual(null, user.Pointses);

            user.Pointses = -1000L;
            Assert.AreEqual(-1000L, user.Pointses);
        }
    }
}