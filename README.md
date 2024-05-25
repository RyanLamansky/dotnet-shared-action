# Shared Action

A simple C# class that allows multiple concurrent requests for the same operation to run that operation just once and share the result.
It's intended to be used in systems that expect real-time results but want to avoid the waste that comes with concurrent processing of the same input.
The more concurrent requests, the greater the benefit: this solution thrives under intense load tests.

This _is not_ a cache: once an action is completed, the results are shared with everyone supplying the same input and discarded.

To use, copy SharedAction/SharedAction.cs directly into your project.
