#What's the big idea?

This is just a wrapper around `StackExchange.Redis.ConnectionMultiplexer` that allows you to store interface "shapes" in redis.

```{c#}
//Here's an interface that represents the shape of our data.
public interface IAwesomeUser
{
    int Id { get; set; }
    string Name { get; set; }
    int AwesomenessLevel { get; set; }
}

[Test]
public void DemoThatAwesomeUser()
{
    //Configure the store...
    Store.Connection = ConnectionMultiplexer.Connect("localhost:6379");

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
}

```