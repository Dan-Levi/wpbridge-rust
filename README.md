
# WordPress Integration Plugin for Rust

Visit [WordPress Integration Plugin for Rust's official website](https://wpbridge.danlevi.no/)

## Synopsis

WordPress Integration Plugin integrates [WordPress](https://wordpress.org/) with [uMod/Oxide](https://umod.org/games/rust) and enabled Rust server admins to show updated player statistics and server information on their WordPress site.

## Features

+ Player and Server statistics exchange between Rust server and WordPress ([WordPress REST API](https://developer.wordpress.com/docs/api/)).<br>
+ Player chat commands

## Installation

+ Press the green Code button above and Download ZIP.<br>
+ Extract the archive.<br>
+ Copy `WPBridge.cs` from extracted folder to `YOUR_RUST_SERVER\oxide\plugins\` where `YOUR_RUST_SERVER` is the path to your rust server.

## Configuration

+ Install [WordPress plugin](https://wordpress.org/plugins/wpbridge-for-rust/), Instructions [here](https://github.com/Dan-Levi/wpbridge-wordpress).
+ Once your Rust server has loaded the plugin, it generates `YOUR_RUST_SERVER\oxide\config\WPBridge.json` config file.
+ Open `WPBridge.json` in your preferred text editor and replace the dummy variables.
+ Reload the plugin using `o.reload WPBridge`.

### Variables
+ `External_IP`: The external IP for your Rust server.<br>
+ `Wordpress_Site_URL`: The URL to your wordpress installation with a trailing slash.<br>
+ `Wordpress_Secret`: The unique secret generated from WordPress plugin.<br>
+ `Player_Data_Update_Interval`: The interval between everytime Rust sends data to WordPress.<br>
+ `Print_Debug_To_Console`: Print debug information to Rust Console. `true`: Debug | `false` No debug.


### Example:

        {
            "External_IP": "98.765.43.21",
            "Wordpress_Site_URL": "http://your-wordpress-site.com/",
            "Wordpress_Secret": "svp*edf&g$s.e",
            "Player_Data_Update_Interval": 30,
            "Print_Debug_To_Console": false
        }

## Player chat commands

+ `/wip.help` See a list of commands.<br>
+ `/wip.isreserved` Check if is reserved or sharing stats.<br>
+ `/wip.reserve` Toggles share/reserve statistics. **BY DEFAULT: share**

## Coming soon

+ More stats, more integration.

## FAQ
+ **Does this plugin have any plugin dependencies?**
  + No.
+ **Why not just communicate directly with database?**
  
  + Some hosts accepts external scripts to query database directly, and some hosts don't.<br>
  By default, remote access to database server is disabled for security reasons on most hosts.

**The upside about this** is that WordPress Integration Plugin doesn't care about the database technology, and shouldn't either.<br>As long as the REST API Endpoint responds correctly **the data that is sent could basically be stored in any kind of database and format.**

## Feedback
You can reach me on [uMod](https://umod.org/user/Murky), [Github](https://github.com/Dan-Levi), [reddit](https://www.reddit.com/user/Danbannan), [Facebook](https://www.facebook.com/danlevi.no/), [Twitter](https://twitter.com/DanLeviH) or even by [e-mail](danbannan@gmail.com).

### **Thanks** for your interest in WordPress Integration Plugin! <3
Visited [WordPress Integration Plugin for Rust's official website](https://wpbridge.danlevi.no/) yet?