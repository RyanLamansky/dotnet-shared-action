# Shared Action

A simple C# class that allows multiple concurrent requests for the same operation to run that operation just once and share the result.
It's intended to be used in systems that expect real-time results but want to avoid the waste that comes with concurrent processing of the same input.

If a longer-term cache is needed, this class can be combined with a dedicated cache service to ensure only one request to the source when the cache expires while under high load.

The code is intended to be copied straight into your project.
