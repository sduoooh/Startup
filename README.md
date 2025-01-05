+ 这是什么？

    一个快速启动器，检索预设的命令并执行。

+ 它能做什么？

    1. 这里的可执行命令分为两种： ```raw``` 和 ```proxy```。
        - ```raw``` 命令简单地根据预设的执行文件地址进行联想词获取（如果需要的话）, 及将文本参数发送给执行文件（如果需要的话）进行执行；
        - ```proxy``` 命令则可以调用已注册的 ```raw``` 命令，支持预设文本的提供。同时，```proxy``` 命令也支持复数个 ```raw``` 及 ```proxy``` 的命令组合自动化执行。
    2. 按```label```进行命令检索和执行的控制。
    3. ```ALT + Q``` 可以从托盘中呼出，也可以收回去。

+ 如何使用？

    请在 [Plugins](https://github.com/sduoooh/Startup/tree/main/Plugins) 文件夹下放置命令文件，并在其后通过提供的 [PluginRegister.py](https://github.com/sduoooh/Startup/blob/main/Plugins/PluginRegister.py) 注册器进行命令的注册。   
    命令文件夹的名字即命令的名称，其中必须放置 ```plugin.json``` 文件以存放基本的命令配置信息。

    1. 注册    
        对于所有命令，```plugin.json```中的结构形式都必须形如并包括以下信息：
        ```
        {
            "name": "test",
            "description": "just test",
            "label": "sfw",
            "starred": false,
            "count": 0,
            "mode": "proxy",

            ...... // 其他字段
        }
        ```
        - 其中：
            1. ```label``` 字段必须为```nsfw``` 或 ```sfw```;   
            2. ```mode```必须为```raw```或```proxy```。


        - 对于```raw```命令，其```plugin.json```结构应为：
            ```
            {
                //上面的基本信息

                ......

                "waitable": true,
                "associatable": false,
                "executable": true,
                "execute_path": "...",
                "source_path": "...",
            }

            ```
            - 其中：
                1. ```waitable``` 指是否需要参数输入。
                2. ```associatable``` 指是否提供联想词服务。特别地，若不提供联想词服务，则参数输入将直接传递。
                3. ```executable``` 指是否可以直接运行程序，或将```source_path``` 作为参数提供给```execute_path``` 进行执行。
                4. ```execute_path``` 字段仅当```executable```为```true```时可选。
        - 对于```proxy```命令，其```plugin.json```结构应为：
            ```
            {
                //上面的基本信息

                ......

                "configure_path": "...",
            }

            ```
            - 其中，```configure_path``` 应指向文件 ```configure.json```，其结构应当为： 

                ```
                {
                    "CommandCombination":
                    [
                        {
                            "name": "test",
                            "waitable": false,
                            "input": "..."
                        }
                    ]
                }

                ```
                - 其中的：
                    1. ```name```应当为已注册命令名。
                    2. 其设置的```waitable```仅当所调用的```raw```命令的该项为```true```时生效，预设的输入仅当其综合的```waitable```为```fasle```时生效。
                    3. 若调用项为```raw```,```waitable```及```input```无效。     
    
    2. 命令   
    假设输入文本为```input```:   
        - 提供联想词服务的程序应当按```-I input```的方式接收```input```, 以```-S input``` 的方式在应执行命令时接收```input```。以下是返回时的返回结构：
            ```    
                //联想词服务返回结构，出错后返回空数组即可。
                public class PluginProcessResult 
                {
                    public string[] result; //返回长度不限
                }

                // 执行返回结构，若支持连续执行则设置Continue为true。
                public class PluginResult 
                {
                    public bool Continue;
                    public bool Success;
                    public string Info;
                }
            ```
        - 不提供联想词服务的程序正常接收即可。

+ 其他信息
    1. 基于```C# 7.3.X``` 版本开发，数据库用的```sqlite```。
    2. 变量名由于一些原因不太符合语义。
    3. 输入词为可用命令时由于是```TextBox```不能局部变色。
    4. 窗口不会自己调整位置。
    5. 偶发托盘图标异常增多又自己消失。
    6. 嵌套```proxy```尚未测试。
            


