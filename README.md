# charge-multi

A simple pre-processor for charge-lnd to set fees based on the overall balance of multiple channels with the same peer.


Edit config.json to your liking.


Add this to your charge.config before your proportional/encourage/discourage sections.
```
[multiple_channels]
node.id = file:///home/lnd/charge-lnd/multiple_channels.list
strategy = use_config
config_file = file:///home/lnd/charge-lnd/multiple_channels.config
```
