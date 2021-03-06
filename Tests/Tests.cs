using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RedisStore;
using StackExchange.Redis;

namespace Tests
{
    interface INoIdProperty { }

    public interface INested
    {
        int Id { get; }
        INested Next { get; set; }
    }

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

            IRedisSet<IQuestion> Favorites { get; set; }

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

            Console.WriteLine("User SomeStuff == null: {0}", user.SomeStuff == null);

            user.SomeStuff.PushHead("Stuff To Add");

            var q = Store.Create<IQuestion>();
            q.Title = "How is babby formed?";
            q.Body = "That's pretty much it.";

            user.AskedQuestions.PushHead(q);
            user.Favorites.Add(q);

            var q2 = Store.Create<IQuestion>();
            q2.Title = "How is babby formed again? [Duplicate]";
            q2.Body = "I forgot the first time and couldn't search.";

            user.AskedQuestions.PushHead(q);

            var u2 = Store.Create<IAwesomeUser>();
            u2.Name = "Bob Bobberson";

            u2.Favorites.Add(q);
            u2.Favorites.Add(q2);

            Console.WriteLine($"User #2 Favorites Count: {u2.Favorites.Count}");

            Console.WriteLine("\nAll Favorited Questions:");
            foreach (var question in user.Favorites.Union(u2.Favorites))
            {
                Console.WriteLine($"Title: {question.Title}");
                Console.WriteLine($"Body: {question.Body}");
            }

            Console.WriteLine("\n Questions Favorited By Everyone");
            foreach (var question in user.Favorites.Intersect(u2.Favorites))
            {
                Console.WriteLine($"Title: {question.Title}");
                Console.WriteLine($"Body: {question.Body}");
            }

            Console.WriteLine("\n What's the difference between User #1's Favorites and User #2's favorites?");
            foreach (var question in user.Favorites.Diff(u2.Favorites))
            {
                Console.WriteLine($"Title: {question.Title}");
                Console.WriteLine($"Body: {question.Body}");
            }

            Console.WriteLine("\n What's the difference between User #2's Favorites and User #1's Favorites?");
            foreach (var question in u2.Favorites.Diff(user.Favorites))
            {
                Console.WriteLine($"Title: {question.Title}");
                Console.WriteLine($"Body: {question.Body}");
            }

            //Implementer.DumpAssembly();
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
                u.AskedQuestions.PushHead(q);
                Assert.AreEqual(i, u.AskedQuestions.Count);
            }
        }

        [Test]
        public void Nested()
        {
            var n = Store.Create<INested>();
            n.Next = Store.Create<INested>();
            Console.WriteLine(n.Id);
        }

        public interface IHasASet
        {
            int Id { get; }
            IRedisSet<string> Strings { get; set; } 
            IRedisSet<IElement> Elements { get; set; } 
        }

        public interface IElement
        {
            int Id { get; }
        }

        [Test]
        public void SetRemoveEntityWorks()
        {
            var hasASet = Store.Create<IHasASet>();

            var key = Store.Create<IElement>();

            hasASet.Elements.Add(key);

            Assert.AreEqual(1, hasASet.Elements.Count);

            Assert.True(hasASet.Elements.Contains(key));

            hasASet.Elements.Remove(key);

            Assert.AreEqual(0, hasASet.Elements.Count);

            Assert.False(hasASet.Elements.Contains(key));
        }

        [Test]
        public void SetRemoveWorks()
        {
            var hasASet = Store.Create<IHasASet>();

            var key = Guid.NewGuid().ToString();

            hasASet.Strings.Add(key);

            Assert.AreEqual(1, hasASet.Strings.Count);

            Assert.True(hasASet.Strings.Contains(key));

            hasASet.Strings.Remove(key);

            Assert.AreEqual(0, hasASet.Strings.Count);

            Assert.False(hasASet.Strings.Contains(key));
        }

        [Test]
        public void StringIdProperty()
        {
            var user = Store.Create<User>();
            user.Name = "Jason Punyon";

            var token = Store.Create<UserToken>();
            token.User = user;
        }

        [Test]
        public void ManyToManyViaSets()
        {
            var tag = Store.Create<ISOTag>();
        }

        [Test]
        public void TypeWithADatetime()
        {
            var w = Store.Create<WithDateTime>();
            w.Date = DateTime.UtcNow;
            Console.WriteLine(w.Date);
        }

        [Test]
        public static async Task AsyncProperties()
        {
            var a = Store.Create<AsyncProperties>();
            a.NameAsync = "Jason Punyon";
            Console.WriteLine(await a.NameAsync);

            a.Score = 100;
            Console.WriteLine(await a.Score);
        }

        [Test]
        public static void UniqueConstraint()
        {
            var u1 = Store.Create<IUniqueUser>();
            u1.Email = "email@example.com";

            var u2 = Store.Create<IUniqueUser>();
            u2.Email = "email1@example.com";
            u2.Email = "email1@example.com";

            u1.Email = "email2@example.com";
            u2.Email = "email@example.com";
        }

        [Test]
        public static async Task AsyncUniqueConstraint()
        {
            var u1 = Store.Create<IUniqueUser>();
            var u2 = Store.Create<IUniqueUser>();

            await Task.WhenAll(
                    u1.EmailAsync = "email@example.com",
                    u2.EmailAsync = "email1@example.com",
                    u2.EmailAsync = "email1@example.com",
                    u1.EmailAsync = "email2@example.com",
                    u2.EmailAsync = "email@example.com"
                );
        }

        [Test]
        [ExpectedException(typeof(UniqueConstraintViolatedException))]
        public static void StringUniqueConstraintFail()
        {
            var u1 = Store.Create<IUniqueUser>();
            var u2 = Store.Create<IUniqueUser>();
            u1.Email = "Hello";
            u2.Email = "Hello";
        }

        [Test]
        [ExpectedException(typeof(UniqueConstraintViolatedException))]
        public static async Task AsyncStringUniqueConstraintFail()
        {
            var u1 = Store.Create<IUniqueUser>();
            var u2 = Store.Create<IUniqueUser>();
            await (Task)(u1.EmailAsync = "Hello");
            await (Task)(u2.EmailAsync = "Hello");
        }

        [Test]
        [ExpectedException(typeof(UniqueConstraintViolatedException))]
        public static void DoubleUniqueConstraintFail()
        {
            var u1 = Store.Create<IUniqueUser>();
            var u2 = Store.Create<IUniqueUser>();
            u1.SomeValue = 3.0;
            u2.SomeValue = 3.0;
        }

        [Test]
        [ExpectedException(typeof(UniqueConstraintViolatedException))]
        public static void RelatedUniqueConstraintFail()
        {
            var u1 = Store.Create<IUniqueUser>();
            var u2 = Store.Create<IUniqueUser>();
            var blah = Store.Create<IRelatedToUnique>();
            blah.SomeData = "YUP YUP";
            u1.Related = blah;
            u2.Related = blah;
        }

        [Test]
        public static void UniqueIndex()
        {
            var u1 = Store.Create<IUniqueUser>();
            u1.IndexedEmail = "email1@example.com";
            u1.IndexedInteger = 101;
            var u2 = Store.Create<IUniqueUser>();
            u2.IndexedEmail = "email2@example.com";
            u2.IndexedInteger = 102;

            var gotIt = Store.IndexQuery<IUniqueUser>(o => o.IndexedEmail == "email1@example.com").Single();
            Console.WriteLine(gotIt.IndexedEmail);
            Console.WriteLine(gotIt.Id);

            var gotInt = Store.IndexQuery<IUniqueUser>(o => o.IndexedInteger == 102).Single();
            Console.WriteLine(gotInt.IndexedEmail);
            Console.WriteLine(gotInt.Id);

            var val = "email2@example.com";
            var gotItAgain = Store.IndexQuery<IUniqueUser>(o => o.IndexedEmail == val).Single();
            Console.WriteLine(gotItAgain.IndexedEmail);
            Console.WriteLine(gotItAgain.Id);

            var valInt = 101;
            var gotIntAgain = Store.IndexQuery<IUniqueUser>(o => o.IndexedInteger == valInt).Single();
            Console.WriteLine(gotIntAgain.IndexedEmail);
            Console.WriteLine(gotIntAgain.Id);
        }
    }

    public interface IUniqueUser
    {
        int Id { get; }

        [UniqueConstraint]
        string Email { get; set; }

        [UniqueConstraint]
        Async<string> EmailAsync { get; set; }

        [UniqueConstraint]
        double SomeValue { get; set; }

        [UniqueConstraint]
        IRelatedToUnique Related { get; set; }

        [UniqueIndex]
        string IndexedEmail { get; set; }

        [UniqueIndex]
        int IndexedInteger { get; set; }
    }

    public interface IRelatedToUnique
    {
        int Id { get; }
        string SomeData { get; set; }
    }

    public interface ISOTag
    {
        int Id { get; }
        string Name { get; set; }
        IRedisSet<ISOQuestion> Questions { get; set; } 
    }

    public interface ISOQuestion
    {
        int Id { get; set; }
        IRedisSet<ISOTag> Tags { get; set; } 
    }

    public interface User
    {
        int Id { get; }
        string Name { get; set; }
    }

    // Define other methods and classes here
    public interface UserToken
    {
        string Id { get; }
        User User { get; set; }
    }

    public interface WithDateTime
    {
        int Id { get; }
        DateTime Date { get; set; }
    }

    public interface AsyncProperties
    {
        int Id { get; }

        Async<string> NameAsync { get; set; }
        Async<int> Score { get; set; } 
    }
}