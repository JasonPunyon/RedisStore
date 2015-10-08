using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RedisStore;

namespace Tests
{
    public interface IUser
    {
        int Id { get; }
        string Name { get; set; }

        int Score { get; set; }
    }

    public interface IAsyncUser
    {
        int Id { get; }

        Task<string> GetName { get; }
        string SetName { set; }
    }

    public class MyFunctions
    {
        public static Func<int, StoreUser> CreateStoreUser = i => new StoreUser() { _id = i };
    }

    public class StoreUser : IUser
    {
        public int _id;

        public int Id {
            get { return _id; }
            set { }
        }

        public string Name
        {
            get { return Store.Database.HashGet($"/IUser/{Id}", "Name"); }
            set { Store.Database.HashSet($"/IUser/{Id}", "Name", value); }
        }

        public int Score { get; set; }

        public static IUser Create()
        {
            var u = new StoreUser();
            var db = Store.Database;
            u.Id = (int) db.HashIncrement("TypeCounters", "IUser");
            db.HashSet($"/IUser/{u.Id}", "Created", DateTime.UtcNow.ToEpochTime());
            return u;
        }

        public static IUser Get(int id)
        {
            var u = new StoreUser();
            u._id = id;
            return u;
        }

        public static IEnumerable<IUser> Enumerate()
        {
            return Enumerable
                .Range(1, (int) Store.Database.HashGet("TypeCounters", "IUser"))
                .Select(MyFunctions.CreateStoreUser);
        }
    }
}