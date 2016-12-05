import os
import time

input = open(os.environ['inputData']).read()
print(input)

# Our tests should expire after 3 seconds, but make sure this runs long enough
count = 0
while count < 10:
  time.sleep(1)
  count = count + 1