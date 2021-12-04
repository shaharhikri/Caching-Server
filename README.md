# Caching-Server

An application that exposes a network service to provide a caching system.<br />
The application listens on port 10011 and accepts TCP connections.<br />
The application is able to handle concurrent connections and perform work for multiple clients at the same time.<br />
The application accepts the following text-based command from clients (assuming UTF8 encoding and line ending using “\r\n”).<br />
Available commands:<br />
●	set cache_key size_in_bytes\r\n<br />
  [size_in_bytes value]<br />
●	get cache_key\r\n<br />

All commands are single line, with arguments separated by spaces.<br />
Line end is marked using “\r\n”.<br />
The set command accepts two arguments, the cache key and the size in bytes of the value to put in the cache.<br />
It will read the value from the network and store the value in the cache to be retrieved by a later get command.<br />
There is not “\r\n” following the value, the next command starts immediately after the end of the value. The server will respond with: “OK\r\n” after a set command has been processed.<br />
The get command has a single argument, the cache key and will return the following output:<br />
OK size_in_bytes\r\n<br />
[size_in_bytes value]<br />

O if the key doesn’t exist, it will output: MISSING\r\n<br />
An example interaction with the server. Line ending are using the \r\n separator.<br />
Client:   $ telnet 127.0.0.1 10011<br />
Client:   Trying 127.0.0.1...<br />
Client:   Connected to 127.0.0.1.<br />
Client:   Escape character is '^]'.<br />
Client:   set email_addr 16<br />
Client:   jobs@ravendb.net<br />
**Server:**   OK<br />
Client:   get email_addr<br />
**Server:**   OK 16<br />
**Server:**   jobs@ravendb.net<br />
Client:   get home<br />
**Server:**   MISSING<br />
Client:   set home 18<br />
Client:   where the heart is<br />
**Server:**   OK<br />
Client:   get home<br />
**Server:**   OK 18<br />
**Server:**   where the heart is<br />

The server holds a maximum of 128 MB of values and when a new value is set that will exceed the maximum memory used, it evicts (the oldest) entries from the cache.<br />
