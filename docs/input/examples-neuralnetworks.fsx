﻿(*** hide ***)
#r "../../src/DiffSharp/bin/Debug/FsAlg.dll"
#r "../../src/DiffSharp/bin/Debug/DiffSharp.dll"
#load "../../packages/FSharp.Charting.0.90.9/FSharp.Charting.fsx"

(**
Neural Networks
===============

[Artificial neural networks](http://en.wikipedia.org/wiki/Artificial_neural_network) are computational architectures based on the properties of biological neural systems, capable of learning and pattern recognition.

Let us create a [feedforward neural network](http://en.wikipedia.org/wiki/Feedforward_neural_network) model and use the DiffSharp library for implementing the [backpropagation](http://en.wikipedia.org/wiki/Backpropagation) algorithm for training it. 

We start by defining our neural network structure.

*)

open DiffSharp.AD
open DiffSharp.AD.Vector
open FsAlg.Generic

// A neuron
type Neuron =
    {mutable w:Vector<D> // Weight vector of this neuron
     mutable b:D} // Bias of this neuron
 
// A layer of neurons
type Layer =
    {n:Neuron[]} // The neurons forming this layer

// A feedforward network of neuron layers
type Network =
    {l:Layer[]} // The layers forming this network

(** 

Each neuron works by taking inputs $\{x_1, \dots, x_n\}$ and calculating the activation (output)

$$$
  a = \sigma \left(\sum_{i} w_i x_i + b\right) \; ,

where $w_i$ are synapse weights associated with each input, $b$ is a bias, and $\sigma$ is an [activation function](http://en.wikipedia.org/wiki/Activation_function) representing the rate of [action potential](http://en.wikipedia.org/wiki/Action_potential) firing in the neuron.

<div class="row">
    <div class="span6 offset2">
        <img src="img/examples-neuralnetworks-neuron.png" alt="Chart" style="width:400px;"/>
    </div>
</div>

The activation function is commonly taken as the [sigmoid function](http://en.wikipedia.org/wiki/Sigmoid_function)

$$$
 \sigma (z) = \frac{1}{1 + e^{-z}} \; ,

due to its "nice" and simple derivative and gain control properties.

Now let us write the network evaluation code and a function for creating a given network configuration and initializing the weights and biases with small random values.

*)

let sigmoid (x:D) = 1. / (1. + exp -x)

let runNeuron (x:Vector<D>) (n:Neuron) =
    x * n.w + n.b
    |> sigmoid

let runLayer (x:Vector<D>) (l:Layer) =
    Array.map (runNeuron x) l.n
    |> vector

let runNetwork (x:Vector<D>) (n:Network) =
    Seq.fold (fun o l -> runLayer o l) x n.l

let rnd = System.Random()

// Initialize a fully connected feedforward neural network
// Weights and biases between -0.5 and 0.5
let createNetwork (inputs:int) (layers:int[]) =
    {l = Array.init layers.Length (fun i -> 
        {n = Array.init layers.[i] (fun _ -> 
            {w = Vector.init
                     (if i = 0 then inputs else layers.[i - 1])
                     (fun _ -> D (-0.5 + rnd.NextDouble()))
             b = D (-0.5 + rnd.NextDouble())})})}
(**

This gives us a highly scalable feedforward network architecture capable of expressing any number of inputs, outputs, and hidden layers we want. The network is fully connected, meaning that each neuron in a layer receives the outputs of all the neurons in the previous layer as its input.

For example, using the code

*)

let net1 = createNetwork 3 [|4; 2|]

(**

would give us the following network with 3 input nodes, a hidden layer with 4 neurons, and an output layer with 2 neurons:

<div class="row">
    <div class="span6 offset2">
        <img src="img/examples-neuralnetworks-network.png" alt="Chart" style="width:400px;"/>
    </div>
</div>

We can also have more than one hidden layer.

For training networks, we will make use of reverse mode AD (the **DiffSharp.AD.Reverse** module) for propagating the error at the output $E$ backwards through the network synapse weights. This will give us the partial derivative of the error at the output with respect to each weight $w_i$ and bias $b_i$ in the network, which we will then use in an update rule

$$$
 \begin{eqnarray*}
 \Delta w_i &=& -\eta \frac{\partial E}{\partial w_i} \; ,\\
 \Delta b_i &=& -\eta \frac{\partial E}{\partial b_i} \; ,\\
 \end{eqnarray*}

where $\eta$ is the learning rate.

It is important to note that the backpropagation algorithm is just a special case of reverse mode AD, with which it shares a common history. Please see the [Reverse AD](gettingstarted-reversead.html) page for an explanation of the usage of adjoints and their backwards propagation.

*)

// The backpropagation algorithm
// n: network to be trained
// eta: learning rate
// epsilon: error threshold
// timeout: maximum number of iterations
// t: training set consisting of input and output vectors
let backprop (n:Network) (eta:float) epsilon (timeout:int) (t:(Vector<float>*Vector<float>)[]) =
    let ta = Array.map (fun x -> Vector.map D (fst x), Vector.map D (snd x)) t
    seq {for i in 0 .. timeout do // A timeout value
            let error = 
                (1. / float ta.Length) * Array.sumBy 
                    (fun t -> Vector.normSq ((snd t) - runNetwork (fst t) n)) ta
            error |> resetTrace
            error |> reverseTrace (D 1.)
            for l in n.l do
                for n in l.n do
                    n.b <- n.b - eta * n.b.A // Update neuron bias
                    n.w <- Vector.map (fun (w:D) -> w - eta * w.A) n.w // Update neuron weights
            if i = timeout then printfn "Failed to converge within %i steps." timeout
            yield float error}
    |> Seq.takeWhile ((<) epsilon)

(**

Using reverse mode AD here has two big advantages: it makes the backpropagation code succinct and straightforward to write and maintain; and it allows us to freely choose activation functions without the burden of coding their derivatives or modifying the backpropagation code accordingly.

We can now test the algorithm by training some networks. 

It is known that [linearly separable](http://en.wikipedia.org/wiki/Linear_separability) rules such as [logical disjunction](http://en.wikipedia.org/wiki/Logical_disjunction) can be learned by a single neuron.

*)
open FSharp.Charting

let trainOR = [|vector [0.; 0.], vector [0.]
                vector [0.; 1.], vector [1.]
                vector [1.; 0.], vector [1.]
                vector [1.; 1.], vector [1.]|]

// 2 inputs, one layer with one neuron
let net2 = createNetwork 2 [|1|]

// Train
let train2 = backprop net2 0.9 0.005 10000 trainOR

// Plot the error during training
Chart.Line train2

(*** hide, define-output: o ***)
printf "val net2 : Network =
  {l = [|{n = [|{w = Vector [|D -0.3042126283; D -0.2509630955|];
                 b = D 0.4165584179;}|];}|];}
val train2 : seq<float>"
(*** include-output: o ***)

(** 

<div class="row">
    <div class="span6 offset1">
        <img src="img/examples-neuralnetworks-chart1.png" alt="Chart" style="width:550px"/>
    </div>
</div>

Linearly inseparable problems such as [exclusive or](http://en.wikipedia.org/wiki/Exclusive_or) require one or more hidden layers to learn.
    
*)

let trainXOR = [|vector [0.; 0.], vector [0.]
                 vector [0.; 1.], vector [1.]
                 vector [1.; 0.], vector [1.]
                 vector [1.; 1.], vector [0.]|]

// 2 inputs, 3 neurons in a hidden layer, 1 neuron in the output layer
let net3 = createNetwork 2 [|3; 1|]

// Train
let train3 = backprop net3 0.9 0.005 10000 trainXOR

// Plot the error during training
Chart.Line train3

(*** hide, define-output: o2 ***)
printf "val net3 : Network =
  {l =
    [|{n =
        [|{w =
            Vector
              [|Adj(-0.3990952149, 7.481450298e-05);
                Adj(0.2626295973, -0.0005625556545)|];
           b = Adj(0.4077099938, -0.0002455469757);};
          {w =
            Vector
              [|Adj(0.3472105762, -0.0003902540939);
                Adj(0.2698220153, -0.0004052317731)|];
           b = Adj(0.03246956809, -0.000286118247);};
          {w =
            Vector
              [|Adj(0.1914881005, -0.0001046784245);
                Adj(-0.1030110692, -7.688368233e-05)|];
           b = Adj(0.05589360816, -5.863152837e-05);}|];};
      {n =
        [|{w =
            Vector
              [|Adj(-0.3930620788, 0.0002184686632);
                Adj(0.4657231793, -0.0001747499928);
                Adj(-0.4974639057, -3.725300124e-05)|];
           b = Adj(-0.4166501578, -8.108279605e-05);}|];}|];}
val train3 : seq<float>"
(*** include-output: o2 ***)

(**
<div class="row">
    <div class="span6 offset1">
        <img src="img/examples-neuralnetworks-chart2.png" alt="Chart" style="width:550px"/>
    </div>
</div>

*)