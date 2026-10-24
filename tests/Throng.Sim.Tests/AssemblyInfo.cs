// Tests are independent and use their own temp folders, so they can run in parallel.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
