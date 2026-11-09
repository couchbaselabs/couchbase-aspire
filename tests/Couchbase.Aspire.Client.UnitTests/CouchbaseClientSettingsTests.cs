namespace Couchbase.Aspire.Client.UnitTests;

public class CouchbaseClientSettingsTests
{
    [Fact]
    public void ApplyConnectionString_DecodesUrlEncodedUsername()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbase://user%40example.com:password@localhost/bucket");

        Assert.Equal("user@example.com", settings.Username);
    }

    [Fact]
    public void ApplyConnectionString_DecodesUrlEncodedPassword()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbase://username:p%40ss%2Fword@localhost/bucket");

        Assert.Equal("p@ss/word", settings.Password);
    }

    [Fact]
    public void ApplyConnectionString_DecodesUrlEncodedBucket()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbase://username:password@localhost/my%20bucket");

        Assert.Equal("my bucket", settings.BucketName);
    }

    [Fact]
    public void ApplyConnectionString_DecodesUrlEncodedUsernamePasswordAndBucket()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbase://user%40name:p%40ss%21word@localhost/my%2Fbucket");

        Assert.Equal("user@name", settings.Username);
        Assert.Equal("p@ss!word", settings.Password);
        Assert.Equal("my/bucket", settings.BucketName);
    }

    [Fact]
    public void ApplyConnectionString_DoesNotAlterUnencodedSegments()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbase://user:pass@localhost/bucket");

        Assert.Equal("user", settings.Username);
        Assert.Equal("pass", settings.Password);
        Assert.Equal("bucket", settings.BucketName);
    }

    [Fact]
    public void ApplyConnectionString_PreservesConnectionStringWithoutUserInfo()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbase://localhost/bucket");

        Assert.Equal("couchbase://localhost", settings.ConnectionString);
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
        Assert.Equal("bucket", settings.BucketName);
    }

    [Fact]
    public void ApplyConnectionString_PreservesQueryParameters()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbase://user%40name:p%40ssword@localhost/bucket?kv_timeout=5000");

        Assert.Equal("couchbase://localhost?kv_timeout=5000", settings.ConnectionString);
        Assert.Equal("user@name", settings.Username);
        Assert.Equal("p@ssword", settings.Password);
    }

    [Fact]
    public void ApplyConnectionString_PreservesHostPort()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbase://username:password@localhost:11210/bucket");

        Assert.Equal("couchbase://localhost:11210", settings.ConnectionString);
        Assert.Equal("username", settings.Username);
        Assert.Equal("password", settings.Password);
        Assert.Equal("bucket", settings.BucketName);
    }

    [Fact]
    public void ApplyConnectionString_PreservesMultipleHostsWithPorts()
    {
        var settings = new CouchbaseClientSettings();

        settings.ApplyConnectionString("couchbases://user:pass@host1:11207,host2:11207/mybucket?option=1");

        Assert.Equal("couchbases://host1:11207,host2:11207?option=1", settings.ConnectionString);
        Assert.Equal("user", settings.Username);
        Assert.Equal("pass", settings.Password);
        Assert.Equal("mybucket", settings.BucketName);
    }
}
