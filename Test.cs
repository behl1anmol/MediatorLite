using System;
using System.Runtime.ExceptionServices;

public class Test {
    public static void Main() {
        try {
            throw new Exception("test");
        } catch (Exception ex) {
            ExceptionDispatchInfo.Throw(ex);
        }
    }
}
