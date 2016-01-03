using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedisStore;
using StackExchange.Redis;

namespace Tests.TwitterDemo
{
    public interface IUser
    {
        string Id { get; }
        string DisplayName { get; set; }
        IRedisSet<ITweet> Tweets { get; set; } 

        IRedisSet<IUser> Following { get; set; } 
        IRedisSet<IUser> Followers { get; set; } 

        IRedisSet<ITweet> Favorites { get; set; } 
    }

    public interface ITweet
    {
        int Id { get; }
        DateTime CreatedDate { get; set; }
        IUser Author { get; set; }
        IRedisSet<IUser> FavoritedBy { get; set; } 
        IRedisSet<IUser> RetweetedBy { get; set; } 
        ITweet InReplyTo { get; set; }
    }

    public static class Twitter
    {
        [Test]
        public static void Demo()
        {
            Store.Connection = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true");
            Store.Connection.GetServer(Store.Connection.GetEndPoints()[0]).FlushAllDatabases();

            var jason = SignUp("JasonPunyon", "JSONP");
            Console.WriteLine(jason.Id);
            Console.WriteLine(jason.DisplayName);

            var codinghorror = SignUp("codinghorror", "Jeff Atwood");
            Console.WriteLine(codinghorror.Id);
            Console.WriteLine(codinghorror.DisplayName);

            var jasonsGreatTweet = jason.Tweet("Isn't twitter just the greatest?");

            codinghorror.Favorite(jasonsGreatTweet);
            codinghorror.Retweet(jasonsGreatTweet);
            codinghorror.Tweet("It sure is, Jason!", jasonsGreatTweet);
        }

        public static IUser SignUp(string handle, string displayName)
        {
            var u = Store.Create<IUser>(handle);
            u.DisplayName = displayName;
            return u;
        }

        public static ITweet Tweet(this IUser user, string body, ITweet inReplyTo = null)
        {
            var t = Store.Create<ITweet>();
            t.Author = user;
            user.Tweets.Add(t);

            if (inReplyTo != null)
            {
                t.InReplyTo = inReplyTo;
            }

            return t;
        }

        public static void Follow(this IUser user, IUser toFollow)
        {
            user.Following.Add(toFollow);
            toFollow.Followers.Add(user);
        }

        public static void Unfollow(this IUser user, IUser toUnfollow)
        {
            user.Following.Remove(toUnfollow);
            toUnfollow.Followers.Remove(user);
        }

        public static void Favorite(this IUser user, ITweet tweet)
        {
            user.Favorites.Add(tweet);
            tweet.FavoritedBy.Add(user);
        }

        public static void Retweet(this IUser user, ITweet tweet)
        {
            user.Tweets.Add(tweet);
            tweet.RetweetedBy.Add(user);
        }

        public static IEnumerable<ITweet> GetStream(this IUser user)
        {
            yield break;
        }
    }
}
