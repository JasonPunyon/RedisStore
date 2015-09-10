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
    public interface IUserTicket
    {
        string Id { get; set; }
        int UserId { get; set; }
    }

    [TestFixture]
    public class Tests
    {
        static ConnectionMultiplexer GetStore()
        {
            var redis = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true");
            redis.GetServer(redis.GetEndPoints()[0]).FlushDatabase(0);
            return redis;
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

        [Test]
        public void CreatingWithAndIntIdWorks()
        {
            var s = GetStore();
            var user = s.Create<IUser>();
            Assert.AreEqual(1, user.Id);
            user = s.Create<IUser>();
            Assert.AreEqual(2, user.Id);
            user = s.Create<IUser>();
            Assert.AreEqual(3, user.Id);
        }

        [Test]
        public void EnumerateWorks()
        {
            var s = GetStore();
            s.Create<IUser>();
            s.Create<IUser>();
            s.Create<IUser>();

            var users = s.Enumerate<IUser>().ToList();

            Assert.AreEqual(3, users.Count);

            Assert.AreEqual(1, users[0].Id);
            Assert.AreEqual(2, users[1].Id);
            Assert.AreEqual(3, users[2].Id);
        }

        [Test]
        public void StringId()
        {
            var s = GetStore();
            s.Create<IUserTicket>();
        }

        [Test]
        public void UncreatedUserDoesNotExist()
        {
            var s = GetStore();
            Assert.False(s.Exists<IUser>(1));
        }

        [Test]
        public void CreatedUserDoesExist()
        {
            var s = GetStore();
            var user = s.Create<IUser>();
            Assert.True(s.Exists<IUser>(user.Id));

            for (var i = 0; i < 10; i++)
            {
                Assert.True(s.Exists<IUser>(s.Create<IUser>().Id));
            }
        }

        [Test]
        public void DeletedUserDoesNotExist()
        {
            var s = GetStore();
            var user = s.Create<IUser>();
            s.Delete(user);

            Assert.False(s.Exists<IUser>(user.Id));
        }

        [Test]
        public void DeletedDoesNotExistOnEnumerate()
        {
            var s = GetStore();
            var user = s.Create<IUser>();
            var user2 = s.Create<IUser>();

            s.Delete(user);

            Assert.False(s.Enumerate<IUser>().Any(u => u.Id == user.Id));
        }
    }
}