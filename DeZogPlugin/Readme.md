# DeZog CSpect Plugin

This Dezog  CSpect Plugin allows to connect [DeZog](https://github.com/maziac/DeZog) with [CSpect](http://www.cspect.org).
I.e. you can use the DeZog IDE and run/debug your program in CSpect.

This plugin establishes a listening socket.
DeZog will connect to this socket when a debug session is started.


# Functionality

The plugin supports DZRP v2.0.0. With the following functionality:
- Continue/StepInto/StepOver/StepOut
- Get memory content
- Get register content
- Setting breakpoints
- Get sprite patterns and attributes


# Plugin Installation

The plugin can be compiled with Visual Studio (19). It has been built with VS on a Mac.

You can find precompiled DLL [here](https://github.com/maziac/DeZogPlugin/releases).

Place the DeZogPlugin.dll in the root directory of CSpect (i.e. at the same level as the CSpect.exe program).
Once you start CSpect it will automatically start the plugin.
If everything works well you will see a message in the console: "DeZog plugin started."

The plugin per default uses socket port 11000. If this is occupied on your system you can change it, see [Socket Configuration](#socket-configuration).

For the DeZog configuration see [DeZog](https://github.com/maziac/DeZog).
Basically you need to create a launch.json with and set the port (if different from default).

You can start CSpect without any (Z80) program. The program is being transferred by DeZog when the debug session is started.

If you use CSpect under macOS or Linux you need to install Mono.
A typical commandline to start CSpect looks like:
~~~
mono CSpect.exe -w4 -zxnext -nextrom -exit -brk -tv
~~~


# Build

If the Plugin.dll changes in a new version of CSpect the DeZog plugin needs to be recompiled.
Therefore the Plugin.dll needs to be referenced inside the DeZog plugin project.
In the Cpect directory a link can be made (ln) to the dll, so it is not required each time to copy the dll.
(Note: a macOS link via desktop is not working, use commandline "ln -s".)

## Release vs. Debug build

There is not much difference in the builds. The Debug build is 22kB in size the Release build is 20kB. Also performance shouldn't make much difference.


# Socket Usage

## Socket Configuration

The DeZog plugin starts to listen for a socket connection on startup at port 11000.
You can change the used port by providing a different port number in the DeZogPlugin.dll.config file.
The DeZogPlugin.dll.config is placed in the same directory as the DeZogPlugin.dll or Cspect.exe.

Here is an examle DeZogPlugin.dll.config:
~~~
<?xml version="1.0" encoding="utf-8"?>
<Settings xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
    <Port>11000</Port>
</Settings>
~~~

Note: If you use the default port 11000 you can even omit the DeZogPlugin.dll.config file.


## Socket Protocol

Please see [DZRP-DeZog Remote Protocol](https://github.com/maziac/DeZog/blob/master/design/DeZogProtocol.md).



# Acknowledgements

The plugin is based on the example plugin from Mike Dailly's [CSpect](http://www.cspect.org).
It also uses some code from Threetwosevensixseven's [CSpectPlugins](https://github.com/Threetwosevensixseven/CSpectPlugins).



