using System;
using System.Dynamic;
using System.Linq;
using NUnit.Framework;
using RedisStore;
using StackExchange.Redis;

namespace Tests
{
    interface INoIdProperty { }

    [TestFixture]
    public class Tests
    {
        //Here's an interface that represents the shape of our data.
        public interface IAwesomeUser
        {
            int Id { get; }
            string Name { get; set; }
            int AwesomenessLevel { get; set; }
            IRedisList<string> SomeStuff { get; set; }
            
            IRedisList<IQuestion> AskedQuestions { get; set; }
        }

        public interface IQuestion
        {
            int Id { get; }
            string Title { get; set; }
            string Body { get; set; }
        }

        //[TearDown]
        //public void TearDown()
        //{
        //    Implementer.ab.Save("StoreImplementations.dll");
        //}

        [Test]
        public void DemoThatAwesomeUser()
        {
            //Configure the connection...
            Store.Connection = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true");

            //Create an awesome user.
            var user = Store.Create<IAwesomeUser>();

            //They've got an id...
            Console.WriteLine(user.Id); //The Id was generated by redis.

            //But they don't have a name...
            Console.WriteLine(user.Name); //Nada.

            //So let's set one...
            user.Name = "Jason Punyon"; //That wrote to redis...(for realz! go check I'll wait).
            
            //And their awesomeness...
            user.AwesomenessLevel = 100;

            //Now that there's a user in there, you can enumerate the users...
            foreach (var u in Store.Enumerate<IAwesomeUser>())
            {
                Console.WriteLine($"User #{u.Id}'s name is {u.Name} and is {u.AwesomenessLevel}% awesome.");
            }

            user.SomeStuff.Add("Stuff To Add");

            var q = Store.Create<IQuestion>();
            q.Title = "How is babby formed?";
            q.Body = "That's pretty much it.";

            user.AskedQuestions.Add(q);

            Implementer.DumpAssembly();
        }

        public Tests()
        {
            Store.Connection = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true");
        }

        [SetUp]
        public void Setup()
        {
            Store.Connection.GetServer(Store.Connection.GetEndPoints()[0]).FlushDatabase(0);   
        }

        [Test]
        [ExpectedException(typeof(NotAnInterfaceException))]
        public void NotAnInterfaceThrows()
        {
            var notAnInterface = Store.Create<string>();
        }

        [Test]
        [ExpectedException(typeof (NoIdPropertyException))]
        public void NoIdPropertyThrows()
        {
            var iNoId = Store.Create<INoIdProperty>();
        }

        [Test]
        public void WritesAndReadsWork()
        {
            var user = Store.Create<IUser>();

            user.Name = "Bob Bobberson";
            Assert.AreEqual("Bob Bobberson", user.Name);
        }

        [Test]
        public void CreateAFew()
        {
            var user = Store.Create<IUser>();
            Assert.AreEqual(1, user.Id);
            user = Store.Create<IUser>();
            Assert.AreEqual(2, user.Id);
            user = Store.Create<IUser>();
            Assert.AreEqual(3, user.Id);
        }

        [Test]
        public void EnumerateWorks()
        {
            Store.Create<IUser>();
            Store.Create<IUser>();
            Store.Create<IUser>();

            var users = Store.Enumerate<IUser>().ToList();

            Assert.AreEqual(3, users.Count);

            Assert.AreEqual(1, users[0].Id);
            Assert.AreEqual(2, users[1].Id);
            Assert.AreEqual(3, users[2].Id);
        }

        [Test]
        public void CountWorks()
        {
            var u = Store.Create<IAwesomeUser>();

            for (var i = 1; i < 4; i++)
            {
                var q = Store.Create<IQuestion>();
                u.AskedQuestions.Add(q);
                Assert.AreEqual(i, u.AskedQuestions.Count);
            }
        }
    }
}