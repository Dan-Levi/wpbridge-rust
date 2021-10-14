## Description

WordPress Bridge integrates [WordPress](https://wordpress.org/) with [uMod/Oxide](https://umod.org/games/rust) enabled Rust servers. This makes it possible for Rust admins to show updated player statistics and server information on their WordPress site.


## Features

+ Player and Server statistics exchange between Rust server and WordPress ([WordPress REST API](https://developer.wordpress.com/docs/api/)).
+ Tested up to **200 players** online simultaneously.
+ Player chat commands.

---

![WPBridge Demo](https://i.imgur.com/aXoVuFq.jpg)

# Visit [WordPress Bridge's official website](https://wpbridge.danlevi.no/)

## Installation

+ Click the Download Button on the [plugin page](https://umod.org/plugins/wordpress-bridge#content)
+ Copy the downloaded `WPBridge.cs` file to `YOUR_RUST_SERVER\oxide\plugins\` where `YOUR_RUST_SERVER` is the path to your rust server.

## Configuration

### Configuration steps

+ Install [WordPress plugin](https://wordpress.org/plugins/wpbridge-for-rust/), Instructions [here](https://github.com/Dan-Levi/wpbridge-wordpress).
+ Once your Rust server has loaded the plugin, it generates `YOUR_RUST_SERVER\oxide\config\WPBridge.json` config file.
+ Open `WPBridge.json` in your preferred text editor and replace the dummy variables.
+ Reload the plugin using `o.reload WPBridge`.
+ The rest is done in [the WordPress plugin](https://wordpress.org/plugins/wpbridge-for-rust/).

---

### Variables
+ `External_IP`: The external IP for your Rust server.
+ `Wordpress_Site_URL`: The URL to your wordpress installation with a trailing slash.
+ `Wordpress_Secret`: The unique secret generated from WordPress plugin.
+ `Player_Data_Update_Interval`: The interval between everytime Rust sends data to WordPress.
+ `Print_Debug_To_Console`: Print debug information to Rust Console. `true`: Debug | `false` No debug.


### Example

        {
            "External_IP": "98.765.43.21",
            "Wordpress_Site_URL": "http://your-wordpress-site.com/",
            "Wordpress_Secret": "svp*edf&g$s.e",
            "Player_Data_Update_Interval": 30,
            "Print_Debug_To_Console": false
        }

## Commands

### Chat commands

+ `/wip.help` See a list of commands.
+ `/wip.isreserved` Check if is reserved or sharing stats.
+ `/wip.reserve` Toggles share/reserve statistics. **BY DEFAULT: share**


## Upcoming features

+ **Loot statistics:** The different resources and containers that players loot.
+ **Kill feed implementation:** Displays a kill feed on your WordPress site showing who killed who.

## FAQ
+ **Does this plugin have any plugin dependencies?**
  + No.
+ **Why not just communicate directly with database?**
  
  + Some hosts accepts external scripts to query database directly, and some hosts don't.
  By default, remote access to database server is disabled for security reasons on most hosts.

**The upside about this** is that WordPress Bridge doesn't care about the database technology, and shouldn't either. As long as the REST API Endpoint responds correctly **the data that is sent could basically be stored in any kind of database and format.**


## Feedback
You can [create a thread](https://umod.org/community/wordpress-bridge/thread/create), submit an issue on [Github](https://github.com/Dan-Levi), reach out on [reddit](https://www.reddit.com/user/Danbannan), [Facebook](https://www.facebook.com/danlevi.no/), [Twitter](https://twitter.com/DanLeviH) or even by [e-mail](danbannan@gmail.com).

## Changelog

### `1.0.124` 
Added loot for all items and resources [Commit on GitHub](https://github.com/Dan-Levi/wpbridge-rust/commit/7ab7da5b4d9fbe5f0af7b481cf267faf973231d1) &nbsp;[Documentation here](https://wpbridge.danlevi.no/shortcode-documentation/)

### `1.0.123` 
Tracking of loot containers. New player stats: killedbynpc,lootcontainer,lootbradheli,loothackable,lootcontainerunderwater [Commit on GitHub](https://github.com/Dan-Levi/wpbridge-rust/commit/7ab7da5b4d9fbe5f0af7b481cf267faf973231d1) and [reddit for details](https://www.reddit.com/r/playrustadmin/comments/q74vsi/plugin_update_wordpress_bridge_v10123_loot/)

### `1.0.122` 
Error logging [Commit on GitHub](https://github.com/Dan-Levi/wpbridge-rust/commit/c494c688958307989102ae39ac45a9ae5a91e3f0) and [reddit for details](https://www.reddit.com/r/playrustadmin/comments/q717fz/plugin_update_wordpress_bridge/)

### `1.0.121` 
Plugin unloading and more readable console. [Commit on GitHub](https://github.com/Dan-Levi/wpbridge-rust/commit/fab5e4f6d82e158a58aa8a98dac3473388f505a1) and [reddit for details](https://www.reddit.com/r/playrustadmin/comments/q70hwk/plugin_update_wordpress_bridge_v10121/)

### `1.0.111` 
Resolved a bug that cleared the player statistics on plugin update [Commit on GitHub](https://github.com/Dan-Levi/wpbridge-for-rust/commit/64ea68e2d75c115e7f36654bcd25f49e63754b90)  and [reddit for details](https://www.reddit.com/r/playrustadmin/comments/q58xas/plugin_update_wpbridge_v10111/)

### `1.0.1` 
Performance update for bandwith and memory usage [Commit on GitHub](https://github.com/Dan-Levi/wpbridge-rust/commit/377b3347d0c9a9e853d30f632ce5019466ea95aa)  and [reddit for details](https://www.reddit.com/r/playrustadmin/comments/q3tw6h/wordpress_integration_plugin_update_101/)

---

## Thank you

### **Thanks** for your interest in WordPress Bridge! <3
Visited [WordPress Bridge's official website](https://wpbridge.danlevi.no/) yet?