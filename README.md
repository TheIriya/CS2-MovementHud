# 中文移动 HUD 与检查点插件

适用于 CS2 的 MG、Surf、BHOP 服务器，基于 [CounterStrikeSharp](https://github.com/roflmuffin/counterstrikesharp)。

## HUD

![alt text](image.png)
- 显示玩家速度和 A/W/S/D/C/Jump 按键状态。
- 观察其他玩家时显示其速度和按键状态。
- `!panel`、`/panel` 或控制台命令 `css_panel` 可切换 HUD。

## 检查点

- `!cp` 保存检查点；最多保存最近 4 个。
- `!tp` 回到最新检查点，`!tp1` 回到上一个，`!tp2` 回到再上一个。
- 支持 `/`、`.` 前缀和 `!!` 静默聊天命令；控制台命令为 `css_cp`、`css_tp`、`css_tp1`、`css_tp2`。
- 检查点保存位置、视角和速度。
- 只有存活且未进入观察状态时才能存点或读点；失败提示为红色。

## 移动速度

首次加载会生成 `movementhud.json`，用于设置地速数值：

```json
{
  "MaxSpeed": 260
}
```

- `MaxSpeed`：启用自定义武器地速时使用的速度，默认 `260`。

游戏可控制的开关可在服务器控制台或 `server.cfg` 设置：

- `mh_speed 1`：启用自定义武器地速，默认开启；设为 `0` 恢复原版武器限速。
- `mh_checkpoints 1`：启用存点和读点命令，默认开启；设为 `0` 禁用这些命令。

修改 `MaxSpeed` 后，从服务器控制台执行 `css_movementhud_reload` 重载文件配置。

## 编译与安装

运行 `dotnet publish -c Release`，将 `bin/Release/net8.0/publish/MovementHud.dll` 放入 `game/csgo/addons/counterstrikesharp/plugins/MovementHud/`，然后重启服务器或加载插件。
