﻿using System.Numerics;
using Continuum.Datastructures;
using Continuum.Datastructures.SingleThreaded.RTree;

namespace Continuum;

/// <summary>
/// This is an abstract class which can be extended to create a type of organism
/// It must be given a Key, Size, Step function, ToString function and FromString function to describe it
/// </summary>
public abstract class Organism : IMinimumBoundable
{
    public delegate void OnRepositionEventHandler(Organism organism, Mbb newMbb);
    public event OnRepositionEventHandler? OnReposition; //can be used to let data structure know when to update its indexing
    /// <summary>
    /// Used to identify type of organism in a file.
    /// </summary>
    public abstract string Key { get; }
    private Vector3 _position;
    private Mbb _mbb;
    
    /// <summary>
    /// The position of the organism.
    /// </summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            Vector3 sizeVector = new Vector3(Size);
            Mbb newMbb = new Mbb(value - sizeVector, value + sizeVector);
            OnReposition?.Invoke(this, newMbb);
            SetMbb(newMbb);
        } 
    }
    
    /// <summary>
    /// Organism is a sphere, so this is the radius.
    /// </summary>
    public float Size { get; }
    
    /// <summary>
    /// Used to identify which organism it is in any visual representation, has no effect besides visual clarity.
    /// </summary>
    public virtual Vector3 Color { get; } = Vector3.Zero;
    
    /// <summary>
    /// Needs this to check if it is in bounds
    /// </summary>
    protected World World { get; }
    
    /// <summary>
    /// Needs this to understand where other organisms are
    /// </summary>
    protected DataStructure DataStructure { get; }
    
    /// <summary>
    /// Needs this to assist in random value generation.
    /// This is the same random class passed when Simulation was made and thus makes use of the same starting seed.
    /// This means that all actions in Organism are deterministic with seed (as long as multithreaded data structure is not being used).
    /// </summary>
    
    public Organism(Vector3 startingPosition, float size, World world, DataStructure dataStructure)
    {
        World = world;
        Size = size; //first assign size so mbb is correctly calculated
        Position = startingPosition;
        
        DataStructure = dataStructure;
        DataStructure.AddOrganism(this);
    }

    /// <summary>
    /// Helper function that is used to copy over any unique, non-standard values to the contents of a new organism.
    /// </summary>
    /// <param name="startingPosition"></param>
    /// <returns></returns>
    public abstract Organism CreateNewOrganism(Vector3 startingPosition);
    
    
    /// <summary>
    /// Gets called every tick by the active data structure, used to express all the logic of an organism.
    /// </summary>
    public abstract void Step();
    
    /// <summary>
    /// Turns the contents of this organism into a string used for writing to file.
    /// </summary>
    /// <returns></returns>
    public new abstract string ToString();
    
    /// <summary>
    /// Gets a string and uses it to set the contents of this organism.
    /// </summary>
    /// <param name="s"></param>
    public abstract void FromString(string s);

    /// <summary>
    /// Moves the organism towards a given location, also accounts for collision checks.
    /// </summary>
    /// <param name="direction"></param>
    public void Move(Vector3 direction)
    {
        if(World.PreciseMovement)
            MoveExact(direction);
        else
            MoveDirect(direction);
    }

    private void MoveDirect(Vector3 direction)
    {
        //Simply add movement towards direction if there is no collision there
        Vector3 newPosition = Position + direction;

        if (!CheckCollision(newPosition))
        {
            //Otherwise no collision, so update position
            Position = newPosition;
        }
    }

    private void MoveExact(Vector3 direction)
    {
        float length = direction.Length();
        if (length == 0) return;

        //Normalize
        direction /= length;

        float minHit = length;

        if (DataStructure.FindFirstCollision(this, direction, length, out float t))
        {
            if (t < minHit)
                minHit = t;
        }
        
        // Small buffer to prevent interpenetration
        float epsilon = 0.001f;
        float moveDist = MathF.Max(0, minHit - epsilon);
        //Actual movement
        Position += direction * moveDist;
    }

    /// <summary>
    /// Looks for room to try to create a new organism of the same type.
    /// Note that if there is no room then no organism will be created and this returns null.
    /// </summary>
    /// <returns></returns>
    public virtual Organism Reproduce()
    {
        //Will do a maximum of 5 attempts
        for (int i = 0; i < 5; i++)
        {
            //Get a direction in a 3D circular radius, length is exactly 1
            float phi = MathF.Acos(2 * Randomiser.NextSingle() - 1) - MathF.PI / 2f;
            float lambda = 2 * MathF.PI * Randomiser.NextSingle();
            float x = MathF.Cos(phi) * MathF.Cos(lambda);
            float y = MathF.Cos(phi) * MathF.Sin(lambda);
            float z = MathF.Sin(phi);
            
            Vector3 direction = new Vector3(x, y, z);
            
            float epsilon = 1.02f;
            Vector3 positiveNewPosition = Position + direction * (Size * epsilon);
            Vector3 negativeNewPosition = Position - direction * (Size * epsilon);
            Vector3 onlyPositiveNewPosition = Position + direction * 2 * (Size * epsilon);
            Vector3 onlyNegativeNewPosition = Position - direction * 2 * (Size * epsilon);

            //Check if both positions are not within another organism
            if (!CheckCollision(positiveNewPosition) && !CheckCollision(negativeNewPosition))
            {
                //Create new organism 
                Organism newOrganism = CreateNewOrganism(positiveNewPosition);
            
                //Push the original organism away in the other direction
                Position = negativeNewPosition;
            
                return newOrganism;
            }
            else if (!CheckCollision(onlyPositiveNewPosition))
            {
                //Create new organism 
                Organism newOrganism = CreateNewOrganism(onlyPositiveNewPosition);
            
                return newOrganism;
            }
            else if (!CheckCollision(onlyNegativeNewPosition))
            {
                //Create new organism 
                Organism newOrganism = CreateNewOrganism(onlyNegativeNewPosition);
            
                return newOrganism;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if this organism would collide with another organism if it would move to the given position
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    private bool CheckCollision(Vector3 position)
    {
        return DataStructure.CheckCollision(this, position);
    }

    /// <summary>
    /// Get minimum bounding box
    /// </summary>
    /// <returns></returns>
    public Mbb GetMbb()
    {
        return _mbb;
    }
    
    /// <summary>
    /// Set minimum bounding box
    /// </summary>
    /// <param name="mbb"></param>
    public void SetMbb(Mbb mbb) //assumes size does not change
    {
        _position = mbb.Minimum + new Vector3(Size);
        _mbb = mbb;
    }

    public bool CheckCollision(Vector3 position, IEnumerable<Organism> otherOrganisms)
    {
        return CheckCollision(position, otherOrganisms, otherOrganism => this == otherOrganism);
    }
    public bool CheckCollision(Vector3 position, IEnumerable<Organism> otherOrganisms, Func<Organism, bool> shouldSkip)
    {
        foreach (Organism otherOrganism in otherOrganisms)
        {
            if (shouldSkip(otherOrganism))
                continue;
            if (CheckCollision(position, otherOrganism))
                return true;
        }
        return false;
    }

    public bool CheckCollision(Vector3 position, Organism otherOrganism)
    {
        //Checks collision by checking distance between circles
        float x = position.X - otherOrganism.Position.X;
        float x2 = x * x;
        float y = position.Y - otherOrganism.Position.Y;
        float y2 = y * y;
        float z = position.Z - otherOrganism.Position.Z;
        float z2 = z * z;
        float sizes = Size + otherOrganism.Size;
        if (x2 + y2 + z2 <= sizes * sizes)
            return true;
        return false;
    }
}