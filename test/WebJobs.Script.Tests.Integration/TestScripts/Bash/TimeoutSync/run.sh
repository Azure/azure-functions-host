input=$(<$inputData)

# Note: bash will flush output if you end with a newline
printf "$input\n"

# Test will run for 3 seconds, but make sure to exit eventually.
for i in {1..30}
do
   sleep 1
done