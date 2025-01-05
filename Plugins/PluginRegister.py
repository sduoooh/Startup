import sys, getopt
from pathlib import Path
from dataclasses import dataclass
from typing import Optional
import json

from sqlalchemy import create_engine, Column, Integer, String, Boolean, ForeignKey
from sqlalchemy.orm import sessionmaker
from sqlalchemy.orm import DeclarativeBase
from sqlalchemy.sql import text

@dataclass
class ProcessResult:
    processStatus: bool
    errorInfo: Optional[str] = None


class Base(DeclarativeBase):
    pass

class Command(Base):
    __tablename__ = 'Commands'
    
    name = Column(String, primary_key=True)
    description = Column(String)
    label = Column(String)
    starred = Column(Boolean, default=False)
    count = Column(Integer, default=0)
    mode = Column(String)

class SingleCommand(Base):
    __tablename__ = 'SingleCommands'
    
    name = Column(String, ForeignKey('Commands.name'), primary_key=True)
    waitable = Column(Boolean, default=False)
    associatable = Column(Boolean, default=False)
    executable = Column(Boolean, default=True)
    execute_path = Column(String, default='')
    source_path = Column(String, default='')

class CommandCombination(Base):
    __tablename__ = 'CommandCombination'
    
    name = Column(String, ForeignKey('Commands.name'), primary_key=True)
    idx = Column(Integer)
    child = Column(String, ForeignKey('Commands.name'))
    waitable = Column(Boolean)
    preset_input = Column(String)

def checkFolderNameVaild(foldername: str):
    return 0 <= foldername.__len__() < 20 and Path.exists(Path("./", foldername, "plugin.json"))


def pluginRegister(foldername: str) -> ProcessResult:
    plugin_path = Path("./", foldername, "plugin.json")
    try: 
        with open(plugin_path, encoding='utf-8') as f:
            plugin_info = json.load(f)
            
        engine = create_engine('sqlite:///plugins.db')
        Base.metadata.create_all(engine)
        
        with sessionmaker(bind=engine)() as session:
            try:
                # 首先检查 Commands 表中是否存在
                existing_command = session.query(Command).filter_by(name=plugin_info['name']).first()
                
                if existing_command:
                    # 更新基本信息
                    existing_command.description = plugin_info['description']
                    existing_command.label = plugin_info['label']
                    existing_command.mode = plugin_info['mode']
                else:
                    # 创建新的基本命令记录
                    new_command = Command(
                        name=plugin_info['name'],
                        description=plugin_info['description'],
                        label=plugin_info['label'],
                        mode=plugin_info['mode'],
                        starred=False,
                        count=0
                    )
                    session.add(new_command)
                    session.flush()  # 确保 new_command 被分配主键
                
                if plugin_info['mode'] == 'raw':
                    # 处理 proxy 模式
                    if not plugin_info['executable']:
                        execute_path = str(Path("./", foldername, plugin_info['execute_path']).absolute())
                    else:
                        execute_path = ''
                    source_path = str(Path("./", foldername, plugin_info['source_path']).absolute())
                
                    # 更新或创建 SingleCommand 记录
                    single_command = session.query(SingleCommand).filter_by(name=plugin_info['name']).first()
                    if single_command:
                        single_command.waitable = plugin_info['waitable']
                        single_command.associatable = plugin_info['associatable']
                        single_command.executable = plugin_info['executable']
                        single_command.execute_path = execute_path
                        single_command.source_path = source_path
                    else:
                        new_single_command = SingleCommand(
                            name=plugin_info['name'],
                            waitable=plugin_info['waitable'],
                            associatable=plugin_info['associatable'],
                            executable=plugin_info['executable'],
                            execute_path=execute_path,
                            source_path=source_path
                        )
                        session.add(new_single_command)
                
                elif plugin_info['mode'] == 'proxy':
                    configure_path = str(Path("./", foldername, plugin_info['configure_path']).absolute())
                    
                    # 删除现有的组合关系
                    session.query(CommandCombination).filter_by(name=plugin_info['name']).delete()
                    
                    # 读取配置文件并创建新的组合关系
                    with open(configure_path, encoding='utf-8') as f:
                        config = json.load(f)
                        for idx, cmd_info in enumerate(config['CommandCombination']):
                            new_combination = CommandCombination(
                                name=plugin_info['name'],
                                idx=idx,
                                child=cmd_info['name'],
                                waitable=cmd_info['waitable'],
                                preset_input=cmd_info['input']
                            )
                            session.add(new_combination)
                
                session.commit()
                return ProcessResult(True, None)
                
            except Exception as e:
                session.rollback()
                return ProcessResult(False, f"Database error: {str(e)}")
                
    except json.JSONDecodeError:
        return ProcessResult(False, "Invalid plugin configuration format")
    except Exception as e:
        return ProcessResult(False, str(e))
    

if __name__ == "__main__":
    opts, _ = getopt.getopt(sys.argv[1:], 'HF:', ["help", "foldername="])
    if opts.__len__() == 0:
        print("Type -H or -help to get correct usage.")
        input("Type someting to exit...")
        exit(0)
    for opt, value in opts:
        if opt in ("-H", "--help"):
            print("Type -F or --foldername with the plugin folder's name to make that registed.")
            exit(0)
        if opt in ("-F", "--foldername"):
            if not checkFolderNameVaild(value):
                print("Foldername vailded.")
                exit(0)
            processResult = pluginRegister(value)
            printMsg = "Plugin Registed!"
            if not processResult.processStatus:
                printMsg = processResult.errorInfo
            print(printMsg)
            exit(0)
        else:
            print("Type -H or -help to get correct usage.")
            exit(0)
            

            