import os

# read the queue message and write to stdout
input = input();
message = "Python script processed queue message '{0}'".format(input)
print(message)

# write to the output binding
f = open(os.environ['output'], 'w')
f.write(input)
