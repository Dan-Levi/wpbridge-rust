
# WPBridge for Rust

## Synopsis

WPBridge integrates your Wordpress site with a Rust server to show player statistics and server information.

## Current features

+ Communication with Wordpress server via WP REST API.

## How to install

+ Press the green Code button above and Download ZIP.
+ Extract the archive.
+ Copy `WPBridge.cs` from extracted folder to `YOUR_RUST_SERVER\oxide\plugins\` where `YOUR_RUST_SERVER` is the path to your rust server.

## How to configure

+ Install WPBridge for Wordpress, following it's instructions from [here](https://github.com/Dan-Levi/wpbridge-wordpress).
+ Once your Rust server has loaded the plugin, it generates `YOUR_RUST_SERVER\oxide\config\WPBridge.json` config file.
+ Open `WPBridge.json` in your preferred text editor and replace the dummy variables.


### The variables
+ `Wordpress_Site_URL`: The url to your wordpress installation with a trailing slash.
+ `Wordpress_Secret`: The secret generated from WPBridge plugin for Wordpress.
+ `Player_Data_Update_Interval`: The interval between everytime Rust sends data to Wordpress.
+ `Print_Debug_To_Console`: Print debug information to Rust Console.


**Example:**

        {
            "Wordpress_Site_URL": "http://your-wordpress-site.com/",
            "Wordpress_Secret": "sdfg*!sadfADTWJ",
            "Player_Data_Update_Interval": 30,
            "Print_Debug_To_Console": false
        }



## Coming soon

+ More stats.

## FAQ
+ **Does this plugin have any plugin dependencies?**
  + No.
+ **Why not just communicate directly with database?**
  
  + Some hosts accepts external scripts to query database directly, and some hosts don't.<br>
  By default, remote access to database server is disabled for security reasons on most hosts.

**The upside about this** is that WPBridge doesn't care about the database technology, and shouldn't either.<br>As long as the REST API Endpoint responds correctly **the data that is sent could basically be stored in any kind of database and format.**<br>

---